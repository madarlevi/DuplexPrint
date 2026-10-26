// Original DuplexPrint implementation. Windows hosts this per-user WinRT task.
// It never submits a physical print job and never runs an interactive SYSTEM service.
#include <windows.h>
#include <roapi.h>
#include <objbase.h>
#include <winrt/Windows.ApplicationModel.h>
#include <winrt/Windows.ApplicationModel.Background.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Printing.Workflow.h>
#include <winrt/Windows.Graphics.Printing.PrintTicket.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.Storage.FileProperties.h>
#include <atomic>
#include <mutex>
#include <string>

using namespace winrt;
using namespace winrt::Windows::ApplicationModel;
using namespace winrt::Windows::ApplicationModel::Background;
using namespace winrt::Windows::Foundation;
using namespace winrt::Windows::Graphics::Printing::Workflow;
using namespace winrt::Windows::Storage;
using namespace winrt::Windows::Storage::Streams;

namespace
{
    constexpr uint64_t maximumBytes = 512ull * 1024 * 1024;

    void LogFailure(hstring const& message) noexcept
    {
        try
        {
            auto file = ApplicationData::Current().LocalFolder().CreateFileAsync(L"capture-error.txt", CreationCollisionOption::ReplaceExisting).get();
            FileIO::WriteTextAsync(file, message).get();
        }
        catch (...) { OutputDebugStringW(L"DuplexPrint capture: could not write the error log.\n"); }
    }

    struct CaptureTask : implements<CaptureTask, IBackgroundTask>
    {
        BackgroundTaskDeferral deferral{ nullptr };
        PrintWorkflowVirtualPrinterSession session{ nullptr };
        IBackgroundTaskInstance instance{ nullptr };
        event_token dataToken{}, cancelToken{};
        std::atomic_bool cancelled{ false };
        std::atomic_bool processing{ false };
        std::atomic_bool finished{ false };
        std::mutex gate;

        void Finish() noexcept
        {
            PrintWorkflowVirtualPrinterSession oldSession{ nullptr };
            IBackgroundTaskInstance oldInstance{ nullptr };
            BackgroundTaskDeferral oldDeferral{ nullptr };
            {
                std::scoped_lock lock(gate);
                if (finished.exchange(true)) return;
                oldSession = std::exchange(session, nullptr);
                oldInstance = std::exchange(instance, nullptr);
                oldDeferral = std::exchange(deferral, nullptr);
            }
            try { if (oldSession) oldSession.VirtualPrinterDataAvailable(dataToken); } catch (...) { }
            try { if (oldInstance) oldInstance.Canceled(cancelToken); } catch (...) { }
            try { if (oldDeferral) oldDeferral.Complete(); } catch (...) { }
        }

        void Run(IBackgroundTaskInstance const& task)
        {
            if (!task) throw hresult_invalid_argument();
            try
            {
                auto details = task.TriggerDetails().try_as<PrintWorkflowVirtualPrinterTriggerDetails>();
                if (!details) throw hresult_invalid_argument(L"Unexpected virtual printer activation.");
                auto currentSession = details.VirtualPrinterSession();
                {
                    std::scoped_lock lock(gate);
                    instance = task;
                    deferral = task.GetDeferral();
                    session = currentSession;
                    cancelToken = task.Canceled([self = get_strong()](auto const&, auto const&)
                    {
                        self->cancelled = true;
                        if (!self->processing) self->Finish();
                    });
                    dataToken = session.VirtualPrinterDataAvailable([self = get_strong()](auto const&, auto const& args)
                    { self->Receive(args); });
                }
                if (!cancelled) currentSession.Start();
            }
            catch (...)
            {
                LogFailure(hresult_error(to_hresult()).message());
                Finish();
                throw;
            }
        }

        fire_and_forget Receive(PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
        {
            auto lifetime = get_strong();
            if (processing.exchange(true)) co_return;
            co_await resume_background();
            auto status = PrintWorkflowSubmittedStatus::Failed;
            StorageFile partial{ nullptr };
            bool committed = false;
            try
            {
                if (cancelled || finished) throw hresult_canceled();
                auto folder = ApplicationData::Current().LocalFolder().CreateFolderAsync(L"Inbox", CreationCollisionOption::OpenIfExists).get();
                GUID identifier{};
                check_hresult(CoCreateGuid(&identifier));
                auto name = to_hstring(identifier);
                partial = folder.CreateFileAsync(name + L".partial", CreationCollisionOption::FailIfExists).get();
                {
                    auto output = partial.OpenAsync(FileAccessMode::ReadWrite).get();
                    auto input = args.SourceContent().GetInputStream();
                    auto contentType = args.SourceContent().ContentType();
                    auto destination = output.GetOutputStreamAt(0);
                    if (contentType == L"application/oxps" || contentType == L"application/vnd.ms-xpsdocument")
                    {
                        auto converter = args.GetPdlConverter(PrintWorkflowPdlConversionType::XpsToPdf);
                        converter.ConvertPdlAsync(args.GetJobPrintTicket(), input, destination).get();
                    }
                    else throw hresult_error(E_INVALIDARG, L"Unsupported captured document format.");
                    if (!destination.FlushAsync().get()) throw hresult_error(E_FAIL, L"Could not flush the captured PDF.");
                    destination.Close();
                    input.Close();
                    if (output.Size() == 0 || output.Size() > maximumBytes) throw hresult_error(HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE), L"Invalid captured PDF size.");
                    output.Close();
                }
                if (cancelled) throw hresult_canceled();
                // Only complete PDFs are visible to the interactive application's watcher.
                partial.RenameAsync(name + L".pdf", NameCollisionOption::FailIfExists).get();
                committed = true;
                // Fixed manifest parameters; no document-supplied executable or arguments.
                FullTrustProcessLauncher::LaunchFullTrustProcessForCurrentAppAsync(L"Capture").get();
                status = PrintWorkflowSubmittedStatus::Succeeded;
            }
            catch (...)
            {
                LogFailure(hresult_error(to_hresult()).message());
                if (partial && !committed) { try { partial.DeleteAsync(StorageDeleteOption::PermanentDelete).get(); } catch (...) { } }
                if (cancelled) status = PrintWorkflowSubmittedStatus::Canceled;
                // A committed PDF is deliberately retained if UI launch fails: open DuplexPrint manually.
            }
            try { args.CompleteJob(status); } catch (...) { LogFailure(hresult_error(to_hresult()).message()); }
            Finish();
        }
    };

    struct CaptureFactory : implements<CaptureFactory, winrt::Windows::Foundation::IActivationFactory>
    {
        IInspectable ActivateInstance() { return make<CaptureTask>(); }
    };
}

extern "C" __declspec(dllexport) HRESULT __stdcall DllGetActivationFactory(HSTRING classId, void** result) noexcept
{
    if (!result) return E_POINTER;
    *result = nullptr;
    try
    {
        hstring name;
        copy_from_abi(name, classId);
        if (name != L"DuplexPrint.CaptureTask") return CLASS_E_CLASSNOTAVAILABLE;
        *result = detach_abi(make<CaptureFactory>());
        return S_OK;
    }
    catch (...) { return to_hresult(); }
}

HRESULT __stdcall DllCanUnloadNow()
{
    return get_module_lock() ? S_FALSE : S_OK;
}
