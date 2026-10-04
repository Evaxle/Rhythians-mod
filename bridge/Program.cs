using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rhythians;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
using var singleton = new Mutex(true, @"Local\RhythiansModBridge", out var first);
if (!first) return;
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (System.ComponentModel.Win32Exception) { }
var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RhythiansMod");
Directory.CreateDirectory(folder);
var gameFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapoRhythia");
var database = new GameDatabase(Path.Combine(gameFolder, "rhythia.db"));
using var api = new Api(folder);
using var profileImage = new ProfileImage(folder);
using var updates = new Updates();
using var daily = new DailyMap();
Task<JsonNode>? dailyRequest = null;
Task? dailyDownload = null;
string? dailyUser = null;
string dailyError = "";
DateTime nextDaily = DateTime.MinValue, nextLocalMaps = DateTime.MinValue;
var nextUpdate = DateTime.MinValue;
Task? updateCheck = null;
Task? updateInstall = null;
CancellationTokenSource? updateCancellation = null;
using var cancellation = new CancellationTokenSource();
var state = new BridgeState();
var cursorPath = Path.Combine(folder, "scores.json");
var queue = File.Exists(cursorPath) ? JsonSerializer.Deserialize<ScoreQueue>(File.ReadAllText(cursorPath)) ?? new() : new ScoreQueue();
if (queue.Cursor == 0) queue.Cursor = database.LatestScore();
try { api.Load(); } catch (Exception) { state.Message = "F8 to connect Rhythians"; }
DateTime nextProfile = DateTime.MinValue, nextMaps = DateTime.MinValue, nextSubmit = DateTime.MinValue;
var localMaps = database.Maps();
var maps = MapCache.Load(folder, api.UserId, localMaps);
var lastGameSeen = DateTime.UtcNow;
var mapsReady = maps.Count > 0;
var profileReady = false;
CancellationTokenSource? signIn = null;
JsonNode? profileDetails = null;
var players = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
string playerRequest = "", playerMap = "";
DateTime nextPlayers = DateTime.MinValue;
var curves = new Dictionary<string, JsonArray>();
Task<JsonNode>? curveRequest = null, playersRequest = null;
string curveMap = "", requestedPlayers = "", requestedPlayerMap = "";
var curveAttempts = new Dictionary<string, DateTime>();
long checkMap = 0;
var mapChecks = new Dictionary<long, string>();
var chartHashes = new Dictionary<long, string>();
var resolved = new Dictionary<string, string>();
var attempted = new Dictionary<long, DateTime>();
var identityPath = Path.Combine(folder, "maps.json");
string lastState = "";
DateTime lastHeartbeat = DateTime.MinValue;
try { if (File.Exists(identityPath)) resolved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(identityPath)) ?? []; } catch (JsonException) { }

while (!cancellation.IsCancellationRequested)
{
    if (Process.GetProcessesByName("rhythia").Length > 0) lastGameSeen = DateTime.UtcNow;
    if (DateTime.UtcNow - lastGameSeen > TimeSpan.FromSeconds(30)) break;
    try
    {
        var menuPath = Path.Combine(folder, "menu.txt");
        if (File.Exists(menuPath) && File.ReadAllText(menuPath).Trim() == "0") { await Task.Delay(1500, cancellation.Token); continue; }
        if (!File.Exists(Path.Combine(folder, "terms-accepted"))) { state.Phase = "terms"; state.Message = "Welcome to Rhythians"; WriteState(); await Task.Delay(1000); continue; }
        var commandPath = Path.Combine(folder, "command.txt");
        var command = File.Exists(commandPath) ? File.ReadAllText(commandPath).Trim() : "";
        if (command.Length > 0) File.Delete(commandPath);
        if (updateCheck?.IsCompleted == true) { await updateCheck; updateCheck = null; }
        if (updateInstall?.IsCompleted == true) { await updateInstall; updateInstall = null; updateCancellation?.Dispose(); updateCancellation = null; }
        if ((command == "updates" || DateTime.UtcNow >= nextUpdate) && updateCheck is null && updateInstall is null) { nextUpdate = DateTime.UtcNow.AddHours(6); updateCheck = updates.Check(cancellation.Token); }
        if (command == "install-update" && updateInstall is null && updateCheck is null) { updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); updateInstall = updates.Install(folder, updateCancellation.Token); }
        if (command == "cancel-update" && updates.Status == "downloading") updateCancellation?.Cancel();
        CollectDaily();
        if (dailyDownload?.IsCompleted == true) { await dailyDownload; dailyDownload = null; nextLocalMaps = DateTime.MinValue; }
        if (api.UserId is not null && dailyRequest is null && DateTime.UtcNow >= nextDaily) { nextDaily = DateTime.UtcNow.AddMinutes(1); dailyUser = api.UserId; dailyRequest = api.Post("/api/mod/daily", new { }, cancellation.Token); }
        if (command == "daily-download" && dailyDownload is null && daily.Data?["completed"]?.GetValue<bool>() != true) dailyDownload = daily.Download(folder, cancellation.Token);
        if (command == "daily-check" && dailyRequest is null && api.UserId is not null) { dailyUser = api.UserId; dailyRequest = api.Post("/api/mod/daily", new { }, cancellation.Token); }
        if (daily.Data is not null && DateTime.UtcNow >= nextLocalMaps) { nextLocalMaps = DateTime.UtcNow.AddSeconds(15); localMaps = database.Maps(); }
        if (command == "profile") profileDetails = await ReadWhileLoading(api.Get("/api/mod/profile?details=1", cancellation.Token));
        if (command == "logout") { api.SignOut(); daily.Data = null; state.Profile = null; profileDetails = null; maps.Clear(); profileReady = mapsReady = false; state.Online = false; state.Phase = "offline"; state.Message = "Rhythians Offline"; await profileImage.Update(null, cancellation.Token); }
        if (command.StartsWith("check ") && long.TryParse(command[6..], out var requested) && maps.All(map => map.Local.Id != requested)) { checkMap = requested; attempted.Remove(requested); mapChecks[requested] = "Checking..."; }
        var connect = Path.Combine(folder, "connect");
        if (File.Exists(connect))
        {
            File.Delete(connect);
            File.Delete(Path.Combine(folder, "cancel-login"));
            state.Phase = "authorizing";
            state.Message = "Authorizing...";
            state.Hint = "Finish signing in in your browser";
            state.Online = false;
            WriteState();
            using (signIn = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
            {
                try { await WaitFor(api.Connect(signIn.Token)); }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { state.Phase = "offline"; state.Message = "Sign-in ended or expired"; state.Hint = "Click Log in to retry"; nextProfile = DateTime.MinValue; continue; }
                finally { signIn = null; }
            }
            nextProfile = nextMaps = DateTime.MinValue;
            nextDaily = DateTime.MinValue;
            if (queue.UserId != api.UserId)
            {
                queue.Pending.Clear();
                queue.Cursor = database.LatestScore();
                state.Profile = null;
                maps.Clear();
                await profileImage.Update(null, cancellation.Token);
            }
            queue.UserId = api.UserId;
            mapsReady = false;
            profileReady = false;
            SaveQueue();
        }
        if (api.UserId is not null && DateTime.UtcNow >= nextProfile)
        {
            nextProfile = DateTime.UtcNow.AddSeconds(30);
            if (!state.Online)
            {
                state.Phase = "loading";
                state.Message = "Loading...";
                state.Hint = "Loading your Rhythians profile";
            }
            var profile = await ReadWhileLoading(api.Get("/api/mod/profile", cancellation.Token));
            state.Profile = profile;
            try { await WaitFor(profileImage.Update(profile["avatar"]?.ToString(), cancellation.Token)); }
            catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException) { }
            profileReady = true;
            if (queue.UserId is null)
            {
                queue.UserId = api.UserId;
                SaveQueue();
            }
        }
        if (api.UserId is not null && DateTime.UtcNow >= nextMaps)
        {
            nextMaps = DateTime.UtcNow.AddMinutes(5);
            localMaps = database.Maps();
            var fetched = new List<MenuMap>();
            var batches = localMaps.Where(m => m.OnlineId != 0).Select(m => m.OnlineId).Distinct().Chunk(100).ToArray();
            for (var i = 0; i < batches.Length; i++)
            {
                if (!mapsReady)
                {
                    state.Phase = "loading";
                    state.Message = "Loading...";
                    state.Hint = $"Loading map ratings {i + 1}/{batches.Length}";
                }
                var response = await ReadWhileLoading(api.Post("/api/mod/maps", new { sourceIds = batches[i] }, cancellation.Token));
                foreach (var item in response["maps"]!.AsArray())
                {
                    if (item is null) continue;
                    var id = item["sourceBeatmapId"]?.GetValue<long>() ?? 0;
                    foreach (var local in localMaps.Where(m => Normalize(m.OnlineId) == Normalize(id))) if (Matches(local, item)) fetched.Add(new(local, item.DeepClone()));
                }
            }
            foreach (var batch in resolved.Values.Distinct().Chunk(100))
            {
                var response = await ReadWhileLoading(api.Post("/api/mod/maps", new { mapIds = batch }, cancellation.Token));
                foreach (var item in response["maps"]!.AsArray())
                {
                    if (item is null) continue;
                    var id = item["id"]!.GetValue<string>();
                    foreach (var local in localMaps.Where(m => resolved.GetValueOrDefault(m.Hash) == id && fetched.All(found => found.Local.Id != m.Id))) if (Matches(local, item)) fetched.Add(new(local, item.DeepClone()));
                }
            }
            maps = fetched;
            MapCache.Save(folder, api.UserId, maps);
            mapsReady = true;
        }
        if (profileReady && mapsReady)
        {
            var selectedPath = Path.Combine(folder, "selection.txt");
            var selectedTitle = File.Exists(selectedPath) ? File.ReadAllText(selectedPath).Trim() : "";
            var selectedIdPath = Path.Combine(folder, "selection-id.txt");
            var selectedId = File.Exists(selectedIdPath) && long.TryParse(File.ReadAllText(selectedIdPath), out var selectedLocalId) ? selectedLocalId : 0;
            var loadedIds = maps.Select(map => map.Local.Id).ToHashSet();
            var candidate = localMaps.Where(local => (local.Id == checkMap || local.Title == selectedTitle || queue.Pending.Any(pending => pending.Score.Hash == local.Hash)) && !loadedIds.Contains(local.Id) && (!attempted.TryGetValue(local.Id, out var time) || DateTime.UtcNow - time > TimeSpan.FromMinutes(10)))
                .OrderByDescending(local => local.Id == checkMap).ThenByDescending(local => local.Title == selectedTitle || queue.Pending.Any(pending => pending.Score.Hash == local.Hash)).FirstOrDefault();
            if (candidate is not null)
            {
                attempted[candidate.Id] = DateTime.UtcNow;
                try
                {
                    var hash = Chart(candidate);
                    var response = await ReadWhileLoading(api.Post("/api/mod/resolve", new { title = candidate.Title, noteCount = candidate.NoteCount, chartHash = hash }, cancellation.Token));
                    var item = response["maps"]?.AsArray().FirstOrDefault();
                    if (item is not null)
                    {
                        maps.Add(new(candidate, item.DeepClone()));
                        MapCache.Save(folder, api.UserId, maps);
                        resolved[candidate.Hash] = item["id"]!.GetValue<string>();
                        File.WriteAllText(identityPath + ".tmp", JsonSerializer.Serialize(resolved));
                        File.Move(identityPath + ".tmp", identityPath, true);
                    }
                    mapChecks[candidate.Id] = item is null ? "Not found - Check again" : "Found";
                    if (candidate.Id == checkMap) checkMap = 0;
                }
                catch (Exception error) when (error is HttpRequestException or InvalidDataException or IOException || error is TaskCanceledException && !cancellation.IsCancellationRequested) { mapChecks[candidate.Id] = "Retry check"; }
            }
            var visiblePath = Path.Combine(folder, "players.txt");
            var visible = File.Exists(visiblePath) ? File.ReadAllText(visiblePath) : "";
            var selectedMap = maps.FirstOrDefault(map => selectedId != 0 ? map.Local.Id == selectedId : map.Local.Title == selectedTitle)?.Data["id"]?.ToString() ?? "";
            if (playersRequest?.IsCompleted == true)
            {
                try {
                    var response = await playersRequest;
                    players.Clear();
                    foreach (var player in response["players"]?.AsArray() ?? []) if (player?["name"] is not null) players[player["name"]!.ToString()] = player.DeepClone();
                } catch (Exception error) when (error is HttpRequestException or InvalidDataException or TaskCanceledException) { }
                playerRequest = requestedPlayers; playerMap = requestedPlayerMap; nextPlayers = DateTime.UtcNow.AddMinutes(2);
                playersRequest = null;
            }
            if (playersRequest is null && (visible != playerRequest || selectedMap != playerMap || DateTime.UtcNow >= nextPlayers) && !string.IsNullOrWhiteSpace(visible))
            {
                requestedPlayers = visible; requestedPlayerMap = selectedMap;
                playersRequest = api.Post("/api/mod/players", new { names = visible.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(50).ToArray(), mapId = selectedMap }, cancellation.Token);
            }
            var speedPath = Path.Combine(folder, "speed.txt");
            var speed = File.Exists(speedPath) && double.TryParse(File.ReadAllText(speedPath), out var readSpeed) ? readSpeed : 1;
            if (curveRequest?.IsCompleted == true)
            {
                try { curves[curveMap] = (await curveRequest)["profiles"]!.AsArray(); }
                catch (Exception error) when (error is HttpRequestException or InvalidDataException or TaskCanceledException) { }
                curveRequest = null;
            }
            if ((speed < .5 || speed > 2) && curveRequest is null)
            {
                var cardsPath = Path.Combine(folder, "cards.txt");
                var visibleCards = File.Exists(cardsPath) ? File.ReadAllLines(cardsPath).ToHashSet() : [];
                var needed = maps.Where(map => visibleCards.Contains(map.Local.Id.ToString()) || map.Data["id"]?.ToString() == selectedMap).Select(map => map.Data["id"]!.ToString()).Distinct().OrderByDescending(id => id == selectedMap).FirstOrDefault(id => !curves.ContainsKey(id) && (!curveAttempts.TryGetValue(id, out var attemptedAt) || DateTime.UtcNow - attemptedAt > TimeSpan.FromMinutes(5)));
                if (needed is not null) { curveMap = needed; curveAttempts[needed] = DateTime.UtcNow; curveRequest = api.Post("/api/mod/difficulty", new { mapId = needed }, cancellation.Token); }
            }
        }
        if (profileReady && state.Profile is not null && mapsReady)
        {
            state.Online = state.Profile["canSubmit"]?.GetValue<bool>() == true;
            state.Phase = state.Online ? "online" : "restricted";
            state.Message = state.Online ? "Rhythians Online" : "Scores unavailable";
            state.Hint = state.Online ? "Connected" : "Check your linked account on Rhythians";
            File.Delete(Path.Combine(folder, "last-error.txt"));
        }
        foreach (var storedScore in database.ScoresAfter(queue.Cursor))
        {
            nextDaily = DateTime.MinValue;
            var modePath = Path.Combine(folder, "mode.txt");
            var vr = File.Exists(modePath) && File.ReadAllText(modePath).Trim() == "2";
            var score = vr ? storedScore with { Mode = "vr" } : storedScore;
            queue.Cursor = score.Id;
            var linkedName = state.Profile?["rhythiaUsername"]?.GetValue<string>() ?? "";
            if (api.UserId is not null && !File.Exists(Path.Combine(folder, "scores-paused")) && queue.UserId == api.UserId && ScoreRules.Eligible(score, linkedName))
                queue.Pending.Add(new(Guid.NewGuid(), score));
            SaveQueue();
        }
        if (state.Online && !File.Exists(Path.Combine(folder, "scores-paused")) && queue.UserId == api.UserId && DateTime.UtcNow >= nextSubmit && queue.Pending.Count > 0)
        {
            nextSubmit = DateTime.UtcNow.AddSeconds(15);
            var pending = queue.Pending[0];
            var score = pending.Score;
            var map = maps.FirstOrDefault(m => !string.IsNullOrEmpty(score.Hash) && string.Equals(m.Local.Hash, score.Hash, StringComparison.OrdinalIgnoreCase));
            if (!DateTime.TryParse(score.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var completed) || DateTime.UtcNow - completed.ToUniversalTime() > TimeSpan.FromDays(6))
            {
                queue.Pending.RemoveAt(0);
                SaveQueue();
            }
            else if (map is not null)
            {
                try
                {
                    await ReadWhileLoading(api.Post("/api/mod/scores", new { clientScoreId = pending.Id, sourceBeatmapId = map.Data["sourceBeatmapId"]?.GetValue<long>(), challengeMapId = map.Data["id"]!.GetValue<string>(), chartHash = Chart(map.Local), accuracy = score.Accuracy, misses = score.Misses, speed = score.Speed, cameraMode = score.Mode?.Contains("vr", StringComparison.OrdinalIgnoreCase) == true ? "vr" : score.Spin ? "spin" : "lock", startFrom = score.StartFrom, passed = score.Passed, modifiers = JsonSerializer.Deserialize<string[]>(score.Mods), completedAt = completed.ToUniversalTime().ToString("O"), playerName = score.Player }, cancellation.Token));
                }
                catch (HttpRequestException error) when (error.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity or System.Net.HttpStatusCode.Conflict)
                {
                    File.AppendAllText(Path.Combine(folder, "rejected-scores.txt"), $"{DateTime.UtcNow:O} Score {score.Id}: {error.Message}{Environment.NewLine}");
                }
                queue.Pending.RemoveAt(0);
                SaveQueue();
                nextProfile = nextMaps = DateTime.MinValue;
                nextDaily = DateTime.MinValue;
            }
            else if (queue.Pending.Count > 1) { queue.Pending.RemoveAt(0); queue.Pending.Add(pending); SaveQueue(); }
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
    catch (Exception error)
    {
        state.Online = false;
        profileReady = false;
        state.Phase = "offline";
        state.Message = error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } ? "Log in again" : error is TimeoutException ? "Sign-in expired" : "Connection failed";
        state.Hint = error.Message;
        nextProfile = DateTime.UtcNow.AddSeconds(30);
        if (!mapsReady) nextMaps = nextProfile;
        File.Delete(Path.Combine(folder, "connect"));
        File.WriteAllText(Path.Combine(folder, "last-error.txt"), $"{DateTime.UtcNow:O} {error.GetType().Name}: {error.Message}");
    }
    WriteState();
    await Task.Delay(1000, cancellation.Token);
}
state.Online = false;
state.Phase = "offline";
state.Message = "Rhythians Offline";
WriteState();

async Task WaitFor(Task task)
{
    while (!task.IsCompleted)
    {
        WriteState();
        if (signIn is not null && (File.Exists(Path.Combine(folder, "cancel-login")) || File.Exists(Path.Combine(folder, "connect")))) { File.Delete(Path.Combine(folder, "cancel-login")); signIn.Cancel(); }
        await Task.WhenAny(task, Task.Delay(1000));
        if (Process.GetProcessesByName("rhythia").Length == 0) cancellation.Cancel();
    }
    await task;
}

async Task<T> ReadWhileLoading<T>(Task<T> task)
{
    await WaitFor((Task)task);
    return await task;
}

void SaveQueue()
{
    File.WriteAllText(cursorPath + ".tmp", JsonSerializer.Serialize(queue));
    File.Move(cursorPath + ".tmp", cursorPath, true);
}

string Chart(LocalMap map)
{
    if (!chartHashes.TryGetValue(map.Id, out var hash)) chartHashes[map.Id] = hash = ChartIdentity.Read(gameFolder, map);
    return hash;
}

bool Matches(LocalMap map, JsonNode data)
{
    if (data["noteCount"] is JsonValue value && value.TryGetValue<int>(out var count) && count > 0 && count != map.NoteCount) return false;
    if (data["chartHash"] is not JsonValue identity || !identity.TryGetValue<string>(out var expected)) return true;
    try { return string.Equals(Chart(map), expected, StringComparison.Ordinal); }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
}

void WriteState()
{
    CollectDaily();
    var text = new StringBuilder();
    var p = state.Profile;
    if (api.UserId is not null && daily.Data is JsonNode day)
    {
        var completed = day["completed"]?.GetValue<bool>() == true;
        var installed = localMaps.Any(map => Normalize(map.OnlineId) == Normalize(day["sourceBeatmapId"]?.GetValue<long>() ?? 0));
        var note = completed ? "Passed today" : daily.Downloading ? daily.Message : dailyError.Length > 0 ? dailyError : dailyRequest is not null ? "Checking today's pass..." : installed ? "In library · Not passed today" : string.IsNullOrEmpty(daily.Message) ? "Not passed today" : daily.Message;
        text.AppendLine(string.Join('\t', "Y", Clean(day["title"]?.ToString() ?? "Daily map"), Clean(note), completed ? "Completed" : daily.Downloading ? "Downloading..." : installed ? "Refresh" : "Download map"));
    }
    else if (api.UserId is not null) text.AppendLine(string.Join('\t', "Y", "Daily map", dailyError.Length > 0 ? dailyError : "Loading today's map...", dailyError.Length > 0 ? "Retry" : "Loading..."));
    text.AppendLine(string.Join('\t', "W", updates.Status, Clean(updates.Message), Clean(updates.AvailableVersion)));
    text.AppendLine(string.Join('\t', new[] { "P", state.Online ? "1" : "0", Clean(state.Message), Clean(p?["username"]?.ToString() ?? "Rhythians"), p?["rhp"]?.ToString() ?? "-1", p?["rbp"]?.ToString() ?? "-1", p?["rpl"]?.ToString() ?? "-1", p?["rps"]?.ToString() ?? "-1", p?["rpvr"]?.ToString() ?? "-1" }));
    text.AppendLine(string.Join('\t', "S", Clean(state.Phase), Clean(state.Hint)));
    text.AppendLine(string.Join('\t', "G", GameSettings.Mode(gameFolder), "mode"));
    text.AppendLine(string.Join('\t', "A", profileImage.Key, Clean(string.Join(",", p?["rankIcons"]?.AsArray().Select(icon => icon?.ToString()) ?? []))));
    text.AppendLine(string.Join('\t', "D", p?["globalRank"] ?? 0, p?["challengeLevel"] ?? 0, Clean(p?["rank"]?.ToString() ?? "--"), api.UserId is null ? "0" : "1"));
    var details = profileDetails ?? p;
    foreach (var category in details?["categories"]?.AsArray() ?? []) text.AppendLine(string.Join('\t', "Z", "0", Clean($"{category?["category"]}: level {category?["level"]}")));
    foreach (var score in details?["scores"]?.AsArray() ?? [])
    {
        var mode = score?["cameraMode"]?.ToString() switch { "spin" => "RPS", "vr" => "RPVR", _ => "RPL" };
        var accuracy = score?["accuracy"] is JsonValue accuracyValue && accuracyValue.TryGetValue<double>(out var percent) ? $"{percent:0.00}%" : "--";
        var title = score?["mapTitle"]?.ToString() ?? "Map";
        if (title.Length > 65) title = title[..62] + "...";
        text.AppendLine(string.Join('\t', "Z", "1", Clean($"{title} | {mode} {score?["points"]} | {accuracy}")));
    }
    foreach (var pass in details?["passes"]?.AsArray() ?? []) text.AppendLine(string.Join('\t', "Z", "2", Clean($"{pass?["category"]} L{pass?["level"]} | {pass?["title"]}")));
    foreach (var player in players.Values) text.AppendLine(string.Join('\t', "U", Clean(player["name"]!.ToString()), Clean($"RHP {player["rhp"]} | {player["rank"]} | #{player["globalRank"]} | L{player["challengeLevel"]}"), Clean($"RPL {player["best"]?["lock"]}  RPS {player["best"]?["spin"]}  RPVR {player["best"]?["vr"]}{(player["passed"]?.GetValue<bool>() == true ? "  Passed" : "")}"), Clean(player["mapId"]?.ToString() ?? "--")));
    var byId = maps.GroupBy(m => m.Local.Id).ToDictionary(group => group.Key, group => group.First().Data);
    foreach (var local in localMaps)
    {
        byId.TryGetValue(local.Id, out var d);
        text.AppendLine(string.Join('\t', new[] { "M", local.Id.ToString(), Clean(local.Title), d is null ? "-1" : d["status"]?.ToString() == "legacy" ? "2" : d["isRanked"]?.GetValue<bool>() == true ? "1" : "0", d?["rating"]?.ToString() ?? "-1", d?["rankability"]?.ToString() ?? "-1", d?["maxRewards"]?["lock"]?.ToString() ?? "0", d?["maxRewards"]?["spin"]?.ToString() ?? "0", d?["maxRewards"]?["vr"]?.ToString() ?? "0" }));
        text.AppendLine(string.Join('\t', "T", local.Id, Clean(string.Join(", ", JsonSerializer.Deserialize<string[]>(local.Mapper) ?? [])), local.StarRating));
        if (d is null) { text.AppendLine(string.Join('\t', "K", local.Id, Clean(mapChecks.GetValueOrDefault(local.Id, "Check map")))); continue; }
        text.AppendLine(string.Join('\t', "I", local.Id, d["id"]));
        var passed = d["passedModes"]?.AsArray().Select(mode => mode?.ToString()).ToHashSet() ?? [];
        var mask = (passed.Contains("lock") ? 1 : 0) | (passed.Contains("spin") ? 2 : 0) | (passed.Contains("vr") ? 4 : 0);
        text.AppendLine(string.Join('\t', "B", local.Id, mask, d["best"]?["lock"] ?? 0, d["best"]?["spin"] ?? 0, d["best"]?["vr"] ?? 0));
        foreach (var speed in d["speedProfiles"]?.AsArray() ?? [])
            if (speed is not null) text.AppendLine(string.Join('\t', "R", local.Id, speed["speed"], speed["rewards"]?["lock"] ?? 0, speed["rewards"]?["spin"] ?? 0, speed["rewards"]?["vr"] ?? 0, speed["rating"] ?? -1));
        if (curves.TryGetValue(d["id"]!.ToString(), out var curve)) foreach (var point in curve) if (point is not null) text.AppendLine(string.Join('\t', "V", local.Id, point["speed"], point["rating"]));
        var challenges = d["challenges"]?.AsArray().Select(challenge => $"{challenge?["category"]} L{challenge?["level"]}{(challenge?["passed"]?.GetValue<bool>() == true ? " Passed" : "")}");
        if (challenges is not null) text.AppendLine(string.Join('\t', "C", local.Id, Clean(string.Join(" / ", challenges))));
    }
    var path = Path.Combine(folder, "state.tsv");
    try
    {
        var value = text.ToString();
        if (value == lastState && File.Exists(path))
        {
            if (DateTime.UtcNow - lastHeartbeat > TimeSpan.FromSeconds(5)) { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); lastHeartbeat = DateTime.UtcNow; }
            return;
        }
        File.WriteAllText(path + ".tmp", value, new UTF8Encoding(false));
        File.Move(path + ".tmp", path, true);
        lastState = value;
        lastHeartbeat = DateTime.UtcNow;
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

void CollectDaily()
{
    if (dailyRequest?.IsCompleted != true) return;
    try
    {
        var response = dailyRequest.GetAwaiter().GetResult();
        if (dailyUser == api.UserId) { daily.Data = response["daily"]?.DeepClone(); dailyError = ""; }
    }
    catch (Exception error) when (error is HttpRequestException or InvalidDataException or TaskCanceledException)
    {
        dailyError = "Couldn't check · Retry";
        nextDaily = DateTime.UtcNow.AddSeconds(15);
    }
    dailyRequest = null;
}

static long Normalize(long id) => id > int.MaxValue && id <= uint.MaxValue ? id - 4294967296L : id;
static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? "--" : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
public sealed class BridgeState { public bool Online; public string Phase = "offline"; public string Message = "Rhythians Offline"; public string Hint = "Log in to see your profile and maps"; public JsonNode? Profile; }
public sealed class ScoreQueue { public long Cursor { get; set; } public string? UserId { get; set; } public List<PendingScore> Pending { get; set; } = []; }
public sealed record PendingScore(Guid Id, GameScore Score);
public sealed record MenuMap(LocalMap Local, JsonNode Data);
