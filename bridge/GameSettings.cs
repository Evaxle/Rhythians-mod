using System.Text.Json.Nodes;

namespace Rhythians;

public static class GameSettings
{
    public static int Mode(string folder)
    {
        try
        {
            var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "config.json")));
            return settings?["SpinCamera"]?["Value"]?.GetValue<bool>() == true ? 1 : 0;
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException) { return -1; }
    }
}
