using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClothingPairing.Core;

/// <summary>Per-host memory storage, independent of the host's settings save schedule.</summary>
public sealed class MemoryArchive
{
    public int Version { get; set; } = 2;
    public Dictionary<ulong, CharacterMemory> Characters { get; set; } = [];
    public Dictionary<ulong, FacewearMemory> FacewearCharacters { get; set; } = [];

    public static void Load(string path, SettingsData settings)
    {
        if (!File.Exists(path)) return;
        var normalized = Parse(File.ReadAllText(path));
        settings.Characters = normalized.Characters;
        settings.FacewearCharacters = normalized.FacewearCharacters;
    }

    public static async Task<SettingsData?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(json);
    }

    private static SettingsData Parse(string json)
    {
        var archive = JsonSerializer.Deserialize<MemoryArchive>(json)
            ?? throw new InvalidDataException("Empty accessory memory file.");
        var normalized = new SettingsData
        {
            Version = archive.Version,
            Characters = archive.Characters,
            FacewearCharacters = archive.FacewearCharacters,
        };
        normalized.Normalize();
        return normalized;
    }

    public static void Save(string path, SettingsData settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var json = JsonSerializer.Serialize(new MemoryArchive
            {
                Characters = settings.Characters,
                FacewearCharacters = settings.FacewearCharacters,
            });
            File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
