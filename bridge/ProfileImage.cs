using System.Security.Cryptography;

namespace Rhythians;

public sealed class ProfileImage(string folder) : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private string? current;
    public string Key { get; private set; } = "--";

    public async Task Update(string? address, CancellationToken cancellation)
    {
        if (address == current) return;
        Key = "--";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host is not ("cdn.discordapp.com" or "www.rhythians.com" or "rhythians.com")) { current = null; return; }
        var builder = new UriBuilder(uri);
        if (uri.Host == "cdn.discordapp.com") builder.Path = Path.ChangeExtension(uri.AbsolutePath, ".png");
        using var response = await client.GetAsync(builder.Uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) return;
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
        {
            if (bytes.Length + read > 1024 * 1024) return;
            bytes.Write(buffer, 0, read);
        }
        var data = bytes.ToArray();
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return;
        var key = Convert.ToHexStringLower(SHA256.HashData(data));
        await File.WriteAllBytesAsync(Path.Combine(folder, $"avatar-{key}.png"), data, cancellation);
        Key = key;
        current = address;
    }

    public void Dispose() => client.Dispose();
}
