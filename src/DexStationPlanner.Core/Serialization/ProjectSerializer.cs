using System.Text.Json;
using System.Text.Json.Serialization;
using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Serialization;

/// <summary>Reads and writes .dexstation project files (indented JSON, stable and diff-friendly).</summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".dexstation";
    public const string ModuleFileExtension = ".dexmodule";

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(StationProject project) => JsonSerializer.Serialize(project, Options);

    public static StationProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<StationProject>(json, Options)
            ?? throw new InvalidDataException("The file does not contain a DEX Station Planner project.");
        if (project.SchemaVersion > StationProject.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"This project was saved by a newer version of DEX Station Planner (schema {project.SchemaVersion}). Please update the app.");
        project.SchemaVersion = StationProject.CurrentSchemaVersion;
        return project;
    }

    public static async Task SaveAsync(StationProject project, string path, CancellationToken ct = default)
    {
        project.ModifiedUtc = DateTime.UtcNow;
        var json = Serialize(project);
        // Write to a temp file and swap it in so a crash mid-save never corrupts the user's project.
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct);
        File.Move(temp, path, overwrite: true);
    }

    public static async Task<StationProject> LoadAsync(string path, CancellationToken ct = default) =>
        Deserialize(await File.ReadAllTextAsync(path, ct));

    public static string SerializeModule(ModuleDefinition module) => JsonSerializer.Serialize(module, Options);

    public static ModuleDefinition DeserializeModule(string json) =>
        JsonSerializer.Deserialize<ModuleDefinition>(json, Options)
        ?? throw new InvalidDataException("The file does not contain a module definition.");

    /// <summary>Deep copy through JSON, keeping ids.</summary>
    public static T DeepClone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
