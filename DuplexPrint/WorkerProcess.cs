using System.Diagnostics;
using System.IO;

namespace VirtualDuplexPrinter;

internal static class WorkerProcess
{
    public static async Task RunAsync(ProcessStartInfo info, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.WindowStyle = ProcessWindowStyle.Hidden;
        info.RedirectStandardError = true;
        info.StandardErrorEncoding = System.Text.Encoding.UTF8;
        using var process = Process.Start(info) ?? throw new IOException("A PDF-feldolgozó nem indítható el.");
        var errorTask = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync();
            await errorTask;
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("A nyomtatás megszakadt. Ellenőrizd a Windows nyomtatási sorát, mielőtt újrapróbálod.", cancellationToken);
            throw new TimeoutException("A PDF-feldolgozó túllépte az időkorlátot. " +
                "Ellenőrizd a Windows nyomtatási sorát: egy korábban elküldött feladat még nyomtatódhat.");
        }
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
            throw new IOException($"A PDF-feldolgozás sikertelen (kód: {process.ExitCode}).\n" + error[..Math.Min(error.Length, 2000)]);
    }
}
