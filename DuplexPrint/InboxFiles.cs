using System.IO;

namespace VirtualDuplexPrinter;

internal static class InboxFiles
{
    // Bounded, cancellable waiting also handles a producer copying a large file slowly.
    public static async Task WaitUntilReadyAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        long previousLength = -1;
        DateTime previousWrite = DateTime.MinValue;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    var write = File.GetLastWriteTimeUtc(path);
                    if (stream.Length > 0 && stream.Length == previousLength && write == previousWrite)
                        return;
                    previousLength = stream.Length;
                    previousWrite = write;
                }
                catch (IOException) { previousLength = -1; }
                await Task.Delay(500, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("A PDF 30 másodpercen belül nem vált olvashatóvá. " +
                "Várd meg a másolás végét, majd nyisd meg kézzel: " + path);
        }
    }
}
