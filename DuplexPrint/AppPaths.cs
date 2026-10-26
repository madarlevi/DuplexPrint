using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualDuplexPrinter;

internal static class AppPaths
{
    public const string VirtualPrinterName = "DuplexPrint";
    public static string? PackageFamily
    {
        get
        {
            uint length = 0;
            if (GetCurrentPackageFamilyName(ref length, null) != 122) return null;
            var value = new StringBuilder((int)length);
            return GetCurrentPackageFamilyName(ref length, value) == 0 ? value.ToString() : null;
        }
    }
    public static string DataDirectory => PackageFamily is { } family
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", family, "LocalState")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuplexPrint");
    public static string SettingsFile => Path.Combine(File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"))
        ? AppContext.BaseDirectory : DataDirectory, "settings.json");
    public static bool IsCapturePrinter(string name) => string.Equals(name, VirtualPrinterName, StringComparison.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);
}
