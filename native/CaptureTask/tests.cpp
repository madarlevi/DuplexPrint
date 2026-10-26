// Activation-only tests: no printer installation, capture activation or print submission.
#include <windows.h>
#include <roapi.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.ApplicationModel.Background.h>
#include <iostream>

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    HMODULE module = LoadLibraryExW(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module) return 3;
    try
    {
        auto activate = reinterpret_cast<HRESULT(__stdcall*)(HSTRING, void**)>(GetProcAddress(module, "DllGetActivationFactory"));
        auto canUnload = reinterpret_cast<HRESULT(__stdcall*)()>(GetProcAddress(module, "DllCanUnloadNow"));
        if (!activate || !canUnload) throw winrt::hresult_error(E_FAIL);
        if (activate(nullptr, nullptr) != E_POINTER) throw winrt::hresult_error(E_FAIL);
        void* unknown = reinterpret_cast<void*>(1);
        winrt::hstring wrong(L"Unregistered.Task");
        if (activate(static_cast<HSTRING>(winrt::get_abi(wrong)), &unknown) != CLASS_E_CLASSNOTAVAILABLE || unknown != nullptr)
            throw winrt::hresult_error(E_FAIL);
        {
            winrt::Windows::Foundation::IActivationFactory factory{ nullptr };
            winrt::hstring name(L"DuplexPrint.CaptureTask");
            winrt::check_hresult(activate(static_cast<HSTRING>(winrt::get_abi(name)), winrt::put_abi(factory)));
            auto task = factory.ActivateInstance<winrt::Windows::ApplicationModel::Background::IBackgroundTask>();
            bool rejected = false;
            try { task.Run(nullptr); }
            catch (winrt::hresult_invalid_argument const&) { rejected = true; }
            if (!rejected || canUnload() != S_FALSE) throw winrt::hresult_error(E_FAIL);
        }
        if (canUnload() != S_OK) throw winrt::hresult_error(E_FAIL);
        std::cout << "PASS Capture DLL exports, class isolation, activation, null-trigger rejection, lifetime/unload. No jobs submitted.\n";
        FreeLibrary(module);
        return 0;
    }
    catch (winrt::hresult_error const& error)
    {
        std::wcerr << error.message().c_str() << L" (" << std::hex << error.code().value << L")\n";
        FreeLibrary(module);
        return 1;
    }
}
