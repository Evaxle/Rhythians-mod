using System.Text.Json;

namespace Rhythians;

public static class ScoreRules
{
    public static bool Eligible(GameScore score, string linkedName)
    {
        if (!score.Passed || score.StartFrom != 0 || string.IsNullOrWhiteSpace(score.Hash) || !double.IsFinite(score.Speed) || score.Speed < 0.5 || score.Speed > 2) return false;
        if (!double.IsFinite(score.Accuracy) || score.Accuracy <= 0 || score.Accuracy > 100 || score.Misses < 0) return false;
        if (string.IsNullOrWhiteSpace(linkedName) || !string.Equals(score.Player, linkedName, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var mods = JsonSerializer.Deserialize<string[]>(score.Mods);
            return mods is not null && mods.All(mod => mod is "mod_pitch_lock" or "mod_mirror" or "mod_mirror_y" or "mod_sudden_death" or "mod_ghost" or "mod_chaos" or "mod_360");
        }
        catch (JsonException) { return false; }
    }
}
