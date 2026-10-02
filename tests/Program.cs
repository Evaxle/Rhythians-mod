using Rhythians;
using System.Text.Json;

var score = new GameScore(1, 0, "chart-hash", "Player", DateTime.UtcNow.ToString("O"), 99, 1, 1, false, 0, true, "[]", "online_profile");
Check(ScoreRules.Eligible(score, "player"));
Check(!ScoreRules.Eligible(score with { Passed = false }, "Player"));
Check(!ScoreRules.Eligible(score with { StartFrom = 1 }, "Player"));
Check(!ScoreRules.Eligible(score with { Mods = "[\"mod_no_fail\"]" }, "Player"));
Check(!ScoreRules.Eligible(score with { Speed = double.NaN }, "Player"));
Check(!ScoreRules.Eligible(score with { Speed = 2.1 }, "Player"));
Check(!ScoreRules.Eligible(score, "Another player"));
Check(ScoreRules.Eligible(score with { MapId = -100 }, "Player"));
Check(Updates.Newer("v0.2.0-beta.2", "0.2.0-beta.1"));
Check(Updates.Newer("v0.2.0", "0.2.0-beta.2"));
Check(!Updates.Newer("v0.2.0-beta.2", "0.2.0"));
Check(!Updates.Newer("invalid", "0.2.0-beta.1"));
Check(!Updates.Newer("v0.2.0-beta.1", "0.2.0-beta.1"));
Console.WriteLine("Score eligibility and release ordering passed.");
if (args.Length > 0)
{
    var database = new GameDatabase(Path.Combine(args[0], "rhythia.db"));
    var samples = database.Maps().Where(map => map.Title.Contains("Baghdad") || map.Title.Contains("Megaflux") || map.OnlineId == 10823 || map.Title.StartsWith("Filthy - Scoop"))
        .Select(map => new { map.Id, sourceId = map.OnlineId, title = map.Title, noteCount = map.NoteCount, chartHash = ChartIdentity.Read(args[0], map) });
    File.WriteAllText(args[1], JsonSerializer.Serialize(samples));
}

static void Check(bool condition)
{
    if (!condition) throw new Exception("Score eligibility check failed.");
}
