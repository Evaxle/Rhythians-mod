using System.Text.Json;

namespace Rhythians;

public static class MapCache
{
    private sealed record Saved(string UserId, List<MenuMap> Maps);

    public static List<MenuMap> Load(string folder, string? userId, List<LocalMap> localMaps)
    {
        var path = Path.Combine(folder, "map-cache.json");
        try
        {
            if (userId is null || !File.Exists(path) || new FileInfo(path).Length > 32_000_000) return [];
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path));
            if (saved?.UserId != userId) return [];
            var local = localMaps.ToDictionary(map => map.Id);
            return saved.Maps.Where(map => local.TryGetValue(map.Local.Id, out var current) && current.Hash == map.Local.Hash).ToList();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public static void Save(string folder, string? userId, List<MenuMap> maps)
    {
        if (userId is null) return;
        var path = Path.Combine(folder, "map-cache.json");
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Saved(userId, maps)));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
