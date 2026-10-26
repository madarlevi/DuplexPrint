using System.IO;

namespace VirtualDuplexPrinter;

internal static class CapturedInbox
{
    public static void Archive(string inbox, string source, bool submitted)
    {
        string directory = Path.GetFullPath(inbox).TrimEnd(Path.DirectorySeparatorChar);
        string file = Path.GetFullPath(source);
        if (!string.Equals(Path.GetDirectoryName(file), directory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Csak a saját beérkezési mappában lévő PDF archiválható.");
        if (!File.Exists(file)) return;
        string history = Path.Combine(Path.GetDirectoryName(directory)!, "History", submitted ? "Submitted" : "Cancelled");
        Directory.CreateDirectory(history);
        File.Move(file, Path.Combine(history, Path.GetFileNameWithoutExtension(file) + "-" + Guid.NewGuid().ToString("N") + ".pdf"));
    }
}
