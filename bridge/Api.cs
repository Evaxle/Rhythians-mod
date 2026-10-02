using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rhythians;

public sealed class Api(string folder) : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri("https://www.rhythians.com"), Timeout = TimeSpan.FromSeconds(65) };
    public string? UserId { get; private set; }

    public void SignOut()
    {
        File.Delete(Path.Combine(folder, "account.bin"));
        client.DefaultRequestHeaders.Authorization = null;
        UserId = null;
    }

    public void Load()
    {
        var path = Path.Combine(folder, "account.bin");
        if (!File.Exists(path)) return;
        var json = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        var account = JsonNode.Parse(json)!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account["token"]!.GetValue<string>());
        UserId = account["userId"]!.GetValue<string>();
    }

    public async Task Connect(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        cancellation = timeout.Token;
        var start = await Post("/api/rhythkit/device/start", new { client = "rhythkit" }, cancellation);
        var url = new Uri(start["verificationUrl"]!.GetValue<string>());
        if (url.Scheme != "https" || (url.Host != "rhythians.com" && url.Host != "www.rhythians.com")) throw new InvalidDataException("Unexpected sign-in address.");
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        var until = DateTime.UtcNow.AddSeconds(Math.Min(120, start["expiresIn"]?.GetValue<int>() ?? 120));
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(3000, cancellation);
            var result = await Post("/api/rhythkit/device/poll", new { deviceCode = start["deviceCode"]!.GetValue<string>() }, cancellation);
            if (result["pending"]?.GetValue<bool>() == true) continue;
            if (result["authorized"]?.GetValue<bool>() != true) throw new InvalidDataException("Sign-in was not approved.");
            var data = Encoding.UTF8.GetBytes(result.ToJsonString());
            var path = Path.Combine(folder, "account.bin");
            File.WriteAllBytes(path + ".tmp", ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser));
            File.Move(path + ".tmp", path, true);
            Load();
            return;
        }
        throw new TimeoutException("Sign-in expired. Press F8 to try again.");
    }

    public async Task<JsonNode> Get(string path, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(path, cancellation);
        return await Read(response, cancellation);
    }

    public async Task<JsonNode> Post(string path, object body, CancellationToken cancellation)
    {
        using var response = await client.PostAsJsonAsync(path, body, cancellation);
        return await Read(response, cancellation);
    }

    private static async Task<JsonNode> Read(HttpResponseMessage response, CancellationToken cancellation)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new HttpRequestException("Connect Rhythians with F8", null, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.Gone) throw new TimeoutException("Sign-in expired. Log in to try again.");
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new HttpRequestException("Sign-in was declined or the linked account is unavailable.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType is not "application/json") throw new InvalidDataException("Rhythians returned an unexpected response. Retrying shortly.");
        var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellation) ?? throw new InvalidDataException("Empty website response.");
        if (json["ok"]?.GetValue<bool>() != true) throw new InvalidDataException("Website request was not accepted.");
        return json;
    }

    public void Dispose() => client.Dispose();
}
