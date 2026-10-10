using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace UsageViewer;

public sealed class SnapshotStore
{
    private const string AppDataDirectoryName = "UsageViewer";
    private const string SettingsFileName = "settings.json";
    private const string SnapshotFileName = "snapshot.json";
    private const string TemporaryFileSuffix = ".tmp";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataDirectoryName);

    public SnapshotStore() => Directory.CreateDirectory(_directory);

    public ViewerSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(_directory, SettingsFileName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(path)) ?? new ViewerSettings()
                : new ViewerSettings();
        }
        catch (JsonException)
        {
            return new ViewerSettings();
        }
        catch (IOException)
        {
            return new ViewerSettings();
        }
    }

    public UsageSnapshot? LoadSnapshot()
    {
        try
        {
            var path = Path.Combine(_directory, SnapshotFileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<UsageSnapshot>(File.ReadAllText(path)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public Task SaveSettingsAsync(ViewerSettings settings) =>
        SaveAtomicallyAsync(SettingsFileName, JsonSerializer.Serialize(settings, JsonOptions));

    public Task SaveSnapshotAsync(UsageSnapshot snapshot) =>
        SaveAtomicallyAsync(SnapshotFileName, JsonSerializer.Serialize(snapshot, JsonOptions));

    private async Task SaveAtomicallyAsync(string fileName, string content)
    {
        var destination = Path.Combine(_directory, fileName);
        var temporary = destination + TemporaryFileSuffix;
        await File.WriteAllTextAsync(temporary, content);
        File.Move(temporary, destination, true);
    }
}
