using System.Text.Json.Nodes;

namespace Rhythians;

public sealed class DailyMap : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) };
    public JsonNode? Data { get; set; }
    public string Message { get; private set; } = "";
    public bool Downloading { get; private set; }

    public static bool AllowedUrl(Uri url) => url.Scheme == "https" && url.Host == "static.rhythia.com" && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo) && Path.GetExtension(url.AbsolutePath).Equals(".sspm", StringComparison.OrdinalIgnoreCase);

    public async Task Download(string folder, CancellationToken token)
    {
        if (Data is null || Downloading) return;
        Downloading = true;
        var name = "daily-" + Guid.NewGuid().ToString("N") + ".sspm";
        var path = Path.Combine(folder, name);
        try
        {
            var url = new Uri(Data["downloadUrl"]!.ToString());
            if (!AllowedUrl(url)) throw new IOException("This daily map has no supported download yet.");
            Message = "Downloading daily map...";
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 134217728) throw new IOException("This download is too large.");
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var target = File.Create(path + ".part"))
            {
                var buffer = new byte[81920];
                long count = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    count += read;
                    if (count > 134217728) throw new IOException("This download is too large.");
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                }
            }
            await using (var file = File.OpenRead(path + ".part"))
            {
                var header = new byte[6];
                await file.ReadExactlyAsync(header, token);
                if (!ValidHeader(header)) throw new IOException("The download is not a supported Rhythia map.");
            }
            File.Move(path + ".part", path);
            await File.WriteAllTextAsync(Path.Combine(folder, "daily-import.txt.tmp"), name, token);
            File.Move(Path.Combine(folder, "daily-import.txt.tmp"), Path.Combine(folder, "daily-import.txt"), true);
            Message = "Importing into Rhythia...";
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UriFormatException or OperationCanceledException)
        {
            Message = error is IOException ? error.Message : "Download failed. Click Download to retry.";
        }
        finally { Downloading = false; if (File.Exists(path + ".part")) File.Delete(path + ".part"); }
    }

    public static bool ValidHeader(byte[] bytes) => bytes.Length >= 6 && bytes[0] == 83 && bytes[1] == 83 && bytes[2] == 43 && bytes[3] == 109 && bytes[4] is 1 or 2 && bytes[5] == 0;
    public void Dispose() => client.Dispose();
}
