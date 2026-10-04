using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.ComponentModel;

namespace Rhythians;

public sealed class Updates : IDisposable
{
    public const string Version = "0.2.0-beta.4";
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };
    private JsonNode? asset;
    public string Status { get; private set; } = "idle";
    public string Message { get; private set; } = "";
    public string AvailableVersion { get; private set; } = "";

    public Updates() => client.DefaultRequestHeaders.UserAgent.ParseAdd("Rhythians-Mod/" + Version);

    public async Task Check(CancellationToken token)
    {
        Status = "checking";
        Message = "Checking for updates...";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var releases = await client.GetFromJsonAsync<JsonArray>("https://api.github.com/repos/Evaxle/Rhythians-mod/releases?per_page=30", timeout.Token) ?? [];
            var release = SelectRelease(releases, Version);
            asset = release?["assets"]?.AsArray().FirstOrDefault(item => item?["name"]?.ToString() == "Rhythians.exe")?.DeepClone();
            if (asset is null) { AvailableVersion = ""; Status = "current"; Message = "You have the latest available beta."; return; }
            AvailableVersion = release!["tag_name"]!.ToString();
            Validate(asset);
            Status = "available";
            Message = $"Rhythians {AvailableVersion} is available. Updating will restart Rhythia.";
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested) { Status = "error"; Message = "Could not check for updates. Try again later."; }
    }

    public async Task Install(string folder, CancellationToken token)
    {
        if (asset is null) { Status = "error"; Message = "Check for updates first."; return; }
        Status = "downloading";
        Message = "Downloading the update. Keep Rhythia open until it finishes.";
        var path = Path.Combine(folder, "Rhythians-update.exe");
        try
        {
            Validate(asset);
            var expectedSize = asset["size"]!.GetValue<long>();
            using var response = await client.GetAsync(asset["browser_download_url"]!.ToString(), HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var target = new FileStream(path + ".part", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long count = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    count += read;
                    if (count > expectedSize) throw new IOException("Unexpected download size.");
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    Message = $"Downloading {count * 100 / expectedSize}% — Cancel stops the download.";
                }
                if (count != expectedSize) throw new IOException("The download was incomplete.");
            }
            await using (var file = File.OpenRead(path + ".part"))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!string.Equals("sha256:" + hash, asset["digest"]!.ToString(), StringComparison.OrdinalIgnoreCase)) throw new IOException("The download could not be verified.");
            }
            File.Move(path + ".part", path, true);
            var root = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
            if (!File.Exists(Path.Combine(root, "rhythia.exe"))) throw new IOException("The game folder could not be found.");
            var start = new ProcessStartInfo(path) { UseShellExecute = true };
            foreach (var argument in new[] { "--update", "--game-dir", root, "--restart" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("The installer did not start.");
            Status = "installing";
            Message = "The installer is opening. It will restart Rhythia after updating.";
            using var launchTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            launchTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(launchTimeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("The installer is still waiting. Finish or close its window, then retry."); }
            var installed = Path.Combine(root, "Rhythians", "installed.json");
            var version = File.Exists(installed) ? JsonNode.Parse(await File.ReadAllTextAsync(installed, token))?["Version"]?.ToString() : null;
            if (version != AvailableVersion.TrimStart('v')) throw new IOException("The installer closed before the update finished. Retry or open the downloaded installer.");
            Status = "current";
            Message = "Update installed. Restart Rhythia to load it.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status = "available"; Message = "Download canceled. You can update later."; }
        catch (Win32Exception error) { Status = "error"; Message = LaunchError(error.NativeErrorCode); }
        catch (Exception error) { Status = "error"; Message = error is IOException ? error.Message : "The update download failed. Retry when your connection is available."; }
        finally { if (File.Exists(path + ".part")) File.Delete(path + ".part"); }
    }

    public static string LaunchError(int code) => code switch
    {
        577 or 1260 or 4551 => "Windows Application Control blocked the installer. A trusted signed build is required on this PC.",
        1223 => "Administrator approval was canceled. Retry when you are ready.",
        5 or 740 => "Windows did not allow the installer to start. Check its administrator prompt, then retry.",
        _ => $"The installer could not start (Windows error {code}). Retry or open the downloaded installer."
    };

    public static JsonNode? SelectRelease(JsonArray releases, string current) => releases
        .Where(item => item?["draft"]?.GetValue<bool>() == false && Newer(item["tag_name"]?.ToString() ?? "", current) && item["assets"]?.AsArray().Any(asset => asset?["name"]?.ToString() == "Rhythians.exe" && asset["state"]?.ToString() == "uploaded") == true)
        .OrderByDescending(item => Parse(item!["tag_name"]!.ToString()).Core).ThenByDescending(item => Parse(item!["tag_name"]!.ToString()).Beta).FirstOrDefault();

    public static bool Newer(string candidate, string current)
    {
        var a = Parse(candidate); var b = Parse(current);
        return a.Core > b.Core || a.Core == b.Core && a.Beta > b.Beta;
    }

    private static (System.Version Core, int Beta) Parse(string value)
    {
        var parts = value.TrimStart('v').Split("-beta.");
        if (!System.Version.TryParse(parts[0], out var core) || parts.Length > 2 || parts.Length == 2 && !int.TryParse(parts[1], out _)) return (new(0, 0, 0), -1);
        return (core, parts.Length == 1 ? int.MaxValue : int.Parse(parts[1]));
    }

    private static void Validate(JsonNode value)
    {
        var url = new Uri(value["browser_download_url"]!.ToString());
        var digest = value["digest"]?.ToString() ?? "";
        if (url.Scheme != "https" || url.Host != "github.com" || !url.AbsolutePath.StartsWith("/Evaxle/Rhythians-mod/releases/download/", StringComparison.Ordinal) || !url.AbsolutePath.EndsWith("/Rhythians.exe", StringComparison.Ordinal) || digest.Length != 71 || !digest.StartsWith("sha256:") || !digest[7..].All(Uri.IsHexDigit) || value["size"]!.GetValue<long>() is <= 0 or > 500_000_000) throw new IOException("The release package is invalid.");
    }

    public void Dispose() => client.Dispose();
}
