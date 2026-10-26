using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfSharp.Pdf;
using VirtualDuplexPrinter;

internal static class UiCapture
{
    // Export the real WPF windows' rendered client areas. No composed mockups,
    // no simulated print completion, no production or mocked print calls.
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string temporary = Directory.CreateTempSubdirectory("DuplexPrint-screenshot-").FullName;
        try
        {
            var window = new MainWindow(null, store: new SettingsStore(Path.Combine(temporary, "settings.json")));
            window.SetInbox(Path.Combine(AppPaths.DataDirectory, "Inbox"));
            window.Show();
            Drain();
            string pdfPath = Path.Combine(temporary, "DuplexPrint-demo.pdf");
            using (var pdf = new PdfDocument()) { for (int i = 0; i < 5; i++) pdf.AddPage(); pdf.Save(pdfPath); }
            window.StartPrintJob(pdfPath);
            Drain();
            Save(window, Path.Combine(directory, "main-window.png"));
            var settings = new SettingsWindow(new SettingsStore(AppPaths.SettingsFile), new AppSettings()) { Owner = window };
            settings.Show();
            Drain();
            Save(settings, Path.Combine(directory, "settings-window.png"));
            settings.Close();
            window.Close();
            application.Shutdown();
            Console.WriteLine("Captured actual WPF client areas; no printing was invoked.");
            return 0;
        }
        finally { Directory.Delete(temporary, true); }
    }
    private static void Drain()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var frame = new DispatcherFrame();
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static void Save(Window window, string path)
    {
        var client = (FrameworkElement)window.Content;
        client.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(client.ActualWidth), (int)Math.Ceiling(client.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(client);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
