using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace VirtualDuplexPrinter;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore store;
    internal AppSettings Result { get; private set; }
    internal SettingsWindow(SettingsStore store, AppSettings settings)
    {
        InitializeComponent();
        this.store = store;
        Result = settings;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsPath.Text = store.FilePath.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? "%LOCALAPPDATA%" + store.FilePath[local.Length..] : store.FilePath;
        SettingsPath.ToolTip = store.FilePath;
        Fill();
    }
    private void Fill()
    {
        ProfileName.Text = Result.Profile.Name;
        FrontOrder.SelectedIndex = (int)Result.Profile.FrontOrder;
        BackOrder.SelectedIndex = (int)Result.Profile.BackOrder;
        RotateBacks.IsChecked = Result.Profile.BackRotationDegrees == 180;
        Instructions.Text = Result.Profile.ReinsertionInstructions;
    }
    private AppSettings Read()
    {
        var value = Result with { Profile = Result.Profile with {
            Name = ProfileName.Text.Trim(), FrontOrder = (PageOrder)FrontOrder.SelectedIndex,
            BackOrder = (PageOrder)BackOrder.SelectedIndex, BackRotationDegrees = RotateBacks.IsChecked == true ? 180 : 0,
            ReinsertionInstructions = Instructions.Text.Trim()
        } };
        value.Validate();
        return value;
    }
    private void Attempt(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Status.Text = ex.Message; }
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Attempt(() => { var value = Read(); store.Save(value); Result = value; DialogResult = true; });
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "DuplexPrint profil (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) Attempt(() => { Result = Result with { Profile = SettingsStore.ImportProfile(dialog.FileName) }; Fill(); Status.Text = "Profil betöltve. Az alkalmazáshoz mentsd a beállításokat."; });
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "DuplexPrint profil (*.json)|*.json", FileName = Result.Profile.Id + ".json", AddExtension = true };
        if (dialog.ShowDialog(this) == true) Attempt(() => File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(Read().Profile, SettingsStore.JsonOptions)));
    }
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        var loaded = store.Load(out string? warning);
        if (warning is null) { Result = loaded; Fill(); }
        Status.Text = warning ?? "A settings.json újraolvasva. A használatához kattints a mentésre.";
    }
    private void EditJson_Click(object sender, RoutedEventArgs e) => Attempt(() =>
    {
        // Never overwrite a malformed existing file merely to open the editor.
        if (!File.Exists(store.FilePath)) store.Save(Read());
        var info = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
        info.ArgumentList.Add(store.FilePath);
        using var process = Process.Start(info);
        Status.Text = "Mentés után itt kattints az Újraolvasás gombra.";
    });
}
