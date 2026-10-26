// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Madár Levente (madarlevi)
// Windows owns the PDF engine. This helper only invokes its documented API.
#include <windows.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Data.Pdf.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.Graphics.Imaging.h>
#include <winrt/Windows.UI.h>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <locale>
#include <stdexcept>
#include <string>

namespace fs = std::filesystem;
using namespace winrt;
using namespace Windows::Data::Pdf;
using namespace Windows::Storage;
using namespace Windows::Storage::Streams;

static void render(const fs::path& input, const fs::path& output, unsigned int dpi)
{
    // No print/spooler APIs are linked or called by this executable.
    if (dpi < 72 || dpi > 600)
        throw std::invalid_argument("Rendering DPI must be between 72 and 600.");
    if (!fs::is_directory(output) || !fs::is_empty(output))
        throw std::invalid_argument("The render directory must exist and be empty.");
    const auto file = StorageFile::GetFileFromPathAsync(fs::absolute(input).wstring()).get();
    const auto document = PdfDocument::LoadFromFileAsync(file).get();
    if (document.PageCount() == 0 || document.IsPasswordProtected())
        throw std::invalid_argument("The PDF is empty or password protected.");

    // Publish the manifest only after ALL pages have rendered successfully.
    // Partial output belongs to the caller's disposable temporary directory.
    std::ofstream manifest(output / L"manifest.tmp", std::ios::binary);
    manifest.exceptions(std::ios::failbit | std::ios::badbit);
    manifest.imbue(std::locale::classic());
    manifest << std::setprecision(12) << "{\"pages\":[";
    for (unsigned int index = 0; index < document.PageCount(); ++index)
    {
        auto page = document.GetPage(index);
        // Size incorporates the PDF crop box and rotation, in 96-DPI units.
        const auto size = page.Size();
        const double width = std::ceil(size.Width * dpi / 96.0);
        const double height = std::ceil(size.Height * dpi / 96.0);
        // Bound per-page bitmap memory to 512 MiB; A3 at 600 DPI fits this limit.
        if (!std::isfinite(width) || !std::isfinite(height) || width <= 0 || height <= 0 ||
            width > 16384 || height > 16384 || width * height > 134217728)
            throw std::invalid_argument("The PDF page exceeds the rendering memory limit at this resolution.");

        PdfPageRenderOptions options;
        options.DestinationWidth(static_cast<unsigned int>(width));
        options.DestinationHeight(static_cast<unsigned int>(height));
        options.BackgroundColor(Windows::UI::Color{255, 255, 255, 255});
        options.IsIgnoringHighContrast(true);
        options.BitmapEncoderId(Windows::Graphics::Imaging::BitmapEncoder::PngEncoderId());
        InMemoryRandomAccessStream stream;
        page.RenderToStreamAsync(stream, options).get();
        if (stream.Size() == 0 || stream.Size() > 536870912)
            throw std::runtime_error("The PDF renderer returned an invalid image size.");
        const auto capacity = static_cast<unsigned int>(stream.Size());
        const auto bytes = stream.GetInputStreamAt(0).ReadAsync(Buffer(capacity), capacity, InputStreamOptions::None).get();
        if (bytes.Length() != capacity)
            throw std::runtime_error("Incomplete PDF page rendering.");
        const auto name = "page-" + std::to_string(index + 1) + ".png";
        std::ofstream image(output / name, std::ios::binary);
        image.exceptions(std::ios::failbit | std::ios::badbit);
        image.write(reinterpret_cast<const char*>(bytes.data()), bytes.Length());
        image.close();
        if (index != 0) manifest << ',';
        manifest << "{\"file\":\"" << name << "\",\"widthDip\":" << size.Width
                 << ",\"heightDip\":" << size.Height << ",\"pixelWidth\":" << width
                 << ",\"pixelHeight\":" << height << '}';
        page.Close();
    }
    manifest << "]}";
    manifest.close();
    fs::rename(output / L"manifest.tmp", output / L"manifest.json");
}

int wmain(int argc, wchar_t** argv)
{
    try
    {
        init_apartment(apartment_type::multi_threaded);
        if (argc != 5 || std::wstring(argv[1]) != L"--render")
            throw std::invalid_argument("Usage: DuplexPrint.PdfWorker.exe --render input.pdf output-directory dpi");
        // Keep this protocol strict: malformed or negative numbers must not wrap.
        const std::wstring resolution(argv[4]);
        if (resolution.empty() || resolution.find_first_not_of(L"0123456789") != std::wstring::npos)
            throw std::invalid_argument("Invalid rendering DPI.");
        const auto dpi = std::stoul(resolution);
        if (dpi < 72 || dpi > 600) throw std::invalid_argument("Invalid rendering DPI.");
        render(argv[2], argv[3], static_cast<unsigned int>(dpi));
        return 0;
    }
    catch (const hresult_error& error)
    {
        std::cerr << "Windows PDF error 0x" << std::hex << static_cast<unsigned int>(error.code().value)
                  << ": " << to_string(error.message()) << '\n';
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; }
    catch (...) { std::cerr << "Unexpected PDF rendering failure.\n"; }
    return 1;
}
