using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VirtualDuplexPrinter;

public enum PageOrder { Ascending, Descending }

public sealed record PrintProfile
{
    [JsonRequired] public string Id { get; init; } = "hp-laserjet-4250tn";
    [JsonRequired] public string Name { get; init; } = "HP LaserJet 4250tn · eredeti sorrend";
    [JsonRequired] public string PrinterModel { get; init; } = "HP LaserJet 4250tn";
    [JsonRequired] public PageOrder FrontOrder { get; init; } = PageOrder.Ascending;
    [JsonRequired] public PageOrder BackOrder { get; init; } = PageOrder.Ascending;
    [JsonRequired] public int BackRotationDegrees { get; init; }
    [JsonRequired] public string ReinsertionInstructions { get; init; } =
        "Várd meg, amíg az első adag minden lapja kijön. Fordítsd meg a teljes köteget: " +
        "az írás felül legyen, a szöveg a nyomtató hátulja felől legyen olvasható. " +
        "A lapok sorrendjét ne változtasd meg. Helyezd vissza a köteget, majd indítsd a második adagot.";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80 || !Regex.IsMatch(Id, @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z") || string.IsNullOrWhiteSpace(Name) || Name.Length > 150 ||
            PrinterModel is null || PrinterModel.Length > 150 || !Enum.IsDefined(FrontOrder) || !Enum.IsDefined(BackOrder) ||
            BackRotationDegrees is not (0 or 180) || string.IsNullOrWhiteSpace(ReinsertionInstructions) || ReinsertionInstructions.Length > 2500)
            throw new InvalidDataException("Érvénytelen nyomtatóprofil. Ellenőrizd a sorrendet, a forgatást és a visszahelyezési útmutatót.");
    }
}

internal static class PagePlan
{
    public static (int[] Fronts, int?[] Backs) Create(int pageCount, PrintProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        profile.Validate();
        var fronts = Enumerable.Range(1, pageCount).Where(p => p % 2 == 1).ToArray();
        var backs = pageCount == 1 ? [] : Enumerable.Range(1, (pageCount + 1) / 2).Select(sheet => sheet * 2 <= pageCount ? (int?)(sheet * 2) : null).ToArray();
        if (profile.FrontOrder == PageOrder.Descending) Array.Reverse(fronts);
        if (profile.BackOrder == PageOrder.Descending) Array.Reverse(backs);
        return (fronts, backs);
    }
}

internal sealed record AppSettings
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public string PrinterName { get; init; } = "";
    [JsonRequired] public PrintProfile Profile { get; init; } = new();
    public void Validate()
    {
        if (SchemaVersion != 1 || PrinterName is null || PrinterName.Length > 500 || Profile is null)
            throw new InvalidDataException("Nem támogatott settings.json verzió vagy érvénytelen beállítás.");
        Profile.Validate();
    }
}

internal sealed class SettingsStore
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<PageOrder>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public string FilePath { get; }
    public SettingsStore(string filePath) => FilePath = Path.GetFullPath(filePath);
    public AppSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(FilePath)) return new();
        try
        {
            if (new FileInfo(FilePath).Length > 131072) throw new InvalidDataException("A beállításfájl túl nagy.");
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? throw new InvalidDataException("Üres beállításfájl.");
            value.Validate();
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            warning = "A settings.json nem olvasható. Az eredeti HP-profil aktív; a fájlt nem írtam felül. " + ex.Message;
            return new();
        }
    }
    public static PrintProfile ImportProfile(string path)
    {
        if (new FileInfo(path).Length > 131072) throw new InvalidDataException("A profilfájl túl nagy.");
        var profile = JsonSerializer.Deserialize<PrintProfile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Üres profilfájl.");
        profile.Validate();
        return profile;
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
