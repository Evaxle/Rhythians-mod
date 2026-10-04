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
Check(Updates.LaunchError(577).Contains("Application Control"));
Check(Updates.LaunchError(1223).Contains("canceled"));
Check(DailyMap.AllowedUrl(new Uri("https://static.rhythia.com/daily.sspm")));
Check(!DailyMap.AllowedUrl(new Uri("https://static.rhythia.com.evil.test/daily.sspm")));
Check(!DailyMap.AllowedUrl(new Uri("http://static.rhythia.com/daily.sspm")));
Check(!DailyMap.AllowedUrl(new Uri("https://static.rhythia.com/daily.exe")));
Check(DailyMap.ValidHeader([83, 83, 43, 109, 2, 0]));
Check(!DailyMap.ValidHeader([77, 90, 0, 0, 0, 0]));
var releases = System.Text.Json.Nodes.JsonNode.Parse("""
[{"tag_name":"v0.3.0","draft":false,"assets":[]},{"tag_name":"v0.2.0-beta.3","draft":false,"assets":[{"name":"Rhythians.exe","state":"uploaded"}]},{"tag_name":"v0.4.0","draft":true,"assets":[{"name":"Rhythians.exe","state":"uploaded"}]}]
""")!.AsArray();
Check(Updates.SelectRelease(releases, "0.2.0-beta.2")?["tag_name"]?.ToString() == "v0.2.0-beta.3");
var cacheFolder = Path.Combine(Path.GetTempPath(), "rhythians-cache-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(cacheFolder);
try
{
    var local = new LocalMap(1, 12, "Map", "[]", "hash", "cache/map", 100);
    MapCache.Save(cacheFolder, "owner", [new(local, System.Text.Json.Nodes.JsonNode.Parse("{\"id\":\"map-id\"}")!)]);
    Check(MapCache.Load(cacheFolder, "owner", [local]).Count == 1);
    Check(MapCache.Load(cacheFolder, "another-user", [local]).Count == 0);
    Check(MapCache.Load(cacheFolder, "owner", [local with { Hash = "changed" }]).Count == 0);
}
finally { Directory.Delete(cacheFolder, true); }
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
