using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace VirtualDuplexPrinter;

public partial class App : Application
{
    private FileSystemWatcher? watcher;
    private Mutex? mutex;
    private bool ownsMutex;
    private readonly CancellationTokenSource shutdown = new();
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> queue = new();
    private MainWindow? currentWindow;
    private string? activeInboxFile;
    private string inbox = string.Empty;
    private bool background;
    private bool installed;
    private EventWaitHandle? activationSignal;
    private RegisteredWaitHandle? activationWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // An isolated worker uses the same self-contained apphost, bypassing UI/watcher/mutex.
        if (e.Args.Length == 3 && e.Args[0] == "--print-pass")
        {
            try
            {
                Task.Run(() => WindowsPdfPrinter.PrintInWorkerAsync(e.Args[1], e.Args[2])).GetAwaiter().GetResult();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                using var error = new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false));
                error.WriteLine(ex.Message);
                Shutdown(1);
            }
            return;
        }
        try
        {
            installed = AppPaths.PackageFamily is not null;
            string instanceName = "Local\\DuplexPrint.Application." + (AppPaths.PackageFamily ?? "Portable");
            mutex = new Mutex(true, instanceName, out ownsMutex);
            if (!ownsMutex)
            {
                try { using var signal = EventWaitHandle.OpenExisting(instanceName + ".Activate"); signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("A DuplexPrint éppen indul. Néhány másodperc múlva nyisd meg újra.", "DuplexPrint"); }
                Shutdown();
                return;
            }
            var args = e.Args.ToList();
            background = args.Remove("--background");
            bool captureActivation = args.Remove("--installed");
            if (captureActivation && !installed) throw new InvalidOperationException("A --installed mód csak a telepített MSIX-csomagban használható.");
            if (installed) background = true;
            bool register = args.Remove("--register-startup");
            bool unregister = args.Remove("--unregister-startup");
            if (installed && (register || unregister)) throw new InvalidOperationException("A telepített kiadás igény szerint indul; nincs szükség külön automatikus indítási bejegyzésre.");
            int watchIndex = args.IndexOf("--watch");
            if (watchIndex >= 0)
            {
                if (watchIndex + 1 >= args.Count)
                    throw new ArgumentException("A --watch után add meg a figyelt mappa elérési útját.");
                inbox = Path.GetFullPath(args[watchIndex + 1]);
                args.RemoveRange(watchIndex, 2);
            }
            else
                inbox = Path.Combine(AppPaths.DataDirectory, "Inbox");
            if (installed && watchIndex >= 0) throw new InvalidOperationException("A telepített kiadás saját, védett felhasználói beérkezési mappát használ.");
            if (args.Count > 1 || args.Any(a => a.StartsWith("--", StringComparison.Ordinal)))
                throw new ArgumentException("Használat: DuplexPrint.exe [dokumentum.pdf] [--watch mappa] [--background] [--register-startup | --unregister-startup]");
            if (register && unregister)
                throw new ArgumentException("Egyszerre csak egy automatikus indítási beállítást adj meg.");
            StartWatcher();
            activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Activate");
            activationWait = ThreadPool.RegisterWaitForSingleObject(activationSignal, (_, _) =>
            {
                if (!shutdown.IsCancellationRequested) Dispatcher.InvokeAsync(() =>
                {
                    if (currentWindow is null) ShowWindow();
                    else { currentWindow.WindowState = WindowState.Normal; currentWindow.Activate(); }
                });
            }, null, Timeout.Infinite, executeOnlyOnce: false);
            if (installed)
                foreach (var file in Directory.EnumerateFiles(inbox, "*.pdf").OrderBy(File.GetCreationTimeUtc)) ObserveFile(file);
            if (register || unregister)
                SetStartup(register);
            if ((!captureActivation && !e.Args.Contains("--background")) || args.Count == 1)
            {
                ShowWindow();
                if (args.Count == 1)
                    currentWindow!.StartPrintJob(args[0]);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("A DuplexPrint nem indítható el.\n\n" + ex.Message, "DuplexPrint", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    private void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "DuplexPrint.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException("Az automatikus indításhoz a DuplexPrint.exe fájlt használd.", exe);
            // A trailing backslash before a closing quote has special command-line meaning.
            // Appending '.' keeps even a drive-root watch path unambiguous.
            key.SetValue("VirtualDuplexPrinter", $"\"{exe}\" --background --watch \"{Path.Combine(inbox, ".")}\"");
        }
        else
            key.DeleteValue("VirtualDuplexPrinter", throwOnMissingValue: false);
    }

    private void StartWatcher()
    {
        Directory.CreateDirectory(inbox);
        watcher = new FileSystemWatcher(inbox, "*.pdf") { NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, e) => ObserveFile(e.FullPath);
        watcher.Renamed += (_, e) => ObserveFile(e.FullPath);
        watcher.Error += (_, _) => Dispatcher.InvokeAsync(() =>
            MessageBox.Show("A mappafigyelő hibát jelzett; néhány fájl kimaradhatott. " +
                "Nyisd meg őket kézzel, vagy indítsd újra a DuplexPrintet.", "DuplexPrint"));
        watcher.EnableRaisingEvents = true;
    }

    private void ObserveFile(string path) => _ = ObserveFileAsync(path);

    private async Task ObserveFileAsync(string path)
    {
        if (!pending.TryAdd(path, 0))
            return;
        try
        {
            await InboxFiles.WaitUntilReadyAsync(path, TimeSpan.FromSeconds(30), shutdown.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                queue.Enqueue(path);
                OpenNextJob();
            });
        }
        catch (OperationCanceledException) { pending.TryRemove(path, out _); }
        catch (Exception ex)
        {
            pending.TryRemove(path, out _);
            try
            {
                if (!shutdown.IsCancellationRequested)
                    await Dispatcher.InvokeAsync(() => MessageBox.Show(ex.Message, "DuplexPrint", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
            catch (Exception notificationError)
            {
                // Shutdown can race the error dialog; do not fault an unobserved watcher task.
                Trace.TraceWarning(notificationError.Message);
            }
        }
    }

    private void ShowWindow()
    {
        currentWindow = new MainWindow();
        MainWindow = currentWindow;
        currentWindow.SetInbox(inbox);
        var window = currentWindow;
        currentWindow.Closed += (_, _) =>
        {
            currentWindow = null;
            if (activeInboxFile is not null)
            {
                if (installed)
                {
                    try { CapturedInbox.Archive(inbox, activeInboxFile, window.WasSubmitted); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { MessageBox.Show("A fogadott PDF nem archiválható; következő indításkor újra megjelenhet.\n" + ex.Message, "DuplexPrint"); }
                }
                pending.TryRemove(activeInboxFile, out _);
            }
            activeInboxFile = null;
            if (queue.Count > 0)
                OpenNextJob();
            else if (!background)
                Shutdown();
        };
        currentWindow.Show();
        currentWindow.Activate();
    }

    private void OpenNextJob()
    {
        if (queue.Count == 0 || currentWindow?.HasJob == true)
            return;
        if (currentWindow is null)
            ShowWindow();
        activeInboxFile = queue.Dequeue();
        try { currentWindow!.StartPrintJob(activeInboxFile); currentWindow.Activate(); }
        catch (Exception ex)
        {
            pending.TryRemove(activeInboxFile, out _);
            activeInboxFile = null;
            MessageBox.Show("A fogadott PDF nem nyitható meg.\n" + ex.Message, "DuplexPrint");
            if (queue.Count > 0) Dispatcher.InvokeAsync(OpenNextJob);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        shutdown.Cancel();
        activationWait?.Unregister(null);
        activationSignal?.Dispose();
        watcher?.Dispose();
        if (ownsMutex)
            mutex?.ReleaseMutex();
        mutex?.Dispose();
        // In-flight watcher callbacks still hold shutdown.Token until their await resumes.
        base.OnExit(e);
    }
}
