using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arqon.Core.Settings;

public interface ISettingsStore
{
    ArqonSettings Load();
    void Save(ArqonSettings settings);
}

public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon", "settings.json");

    public ArqonSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return ArqonSettings.Default();
            return JsonSerializer.Deserialize<ArqonSettings>(File.ReadAllText(path), Options) ?? ArqonSettings.Default();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ArqonSettings.Default();
        }
    }

    public void Save(ArqonSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
