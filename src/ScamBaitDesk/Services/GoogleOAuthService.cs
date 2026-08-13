using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Security.Credentials;

namespace ScamBaitDesk.Services;

public sealed class GoogleOAuthService
{
    private const string VaultResource = "ScamBaitDesk.GoogleOAuth";
    private const string Scope = "https://mail.google.com/";

    public async Task<string> AuthorizeAsync(string username, string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("Enter a Google OAuth desktop client ID in Mail settings first.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var port = GetAvailablePort();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener(); listener.Prefixes.Add(redirect); listener.Start();
        var url = "https://accounts.google.com/o/oauth2/v2/auth?" + Form(new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = Scope,
            ["access_type"] = "offline", ["prompt"] = "consent", ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256", ["state"] = state, ["login_hint"] = username
        });
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];
        var error = context.Request.QueryString["error"];
        var response = Encoding.UTF8.GetBytes("<html><body><h2>ScamBait Desk</h2><p>Authorization received. You may close this tab and return to the app.</p></body></html>");
        context.Response.ContentType = "text/html"; context.Response.ContentLength64 = response.Length; await context.Response.OutputStream.WriteAsync(response, cancellationToken); context.Response.Close();
        if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException($"Google authorization failed: {error}");
        if (returnedState != state || string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("The OAuth response could not be validated.");
        var token = await ExchangeAsync(new Dictionary<string, string> { ["client_id"] = clientId, ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code" }, cancellationToken);
        SaveRefreshToken(username, token.RefreshToken ?? throw new InvalidOperationException("Google did not return a refresh token. Revoke the app grant and try again."));
        return token.AccessToken;
    }

    public async Task<string> GetAccessTokenAsync(string username, string clientId, CancellationToken cancellationToken = default)
    {
        var refresh = LoadRefreshToken(username) ?? throw new InvalidOperationException("Connect the Gmail account with OAuth first.");
        return (await ExchangeAsync(new Dictionary<string, string> { ["client_id"] = clientId, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" }, cancellationToken)).AccessToken;
    }

    private static async Task<TokenResponse> ExchangeAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(); using var content = new FormUrlEncodedContent(values);
        var response = await client.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Google token exchange failed: {json}");
        using var document = JsonDocument.Parse(json);
        return new TokenResponse(document.RootElement.GetProperty("access_token").GetString()!, document.RootElement.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null);
    }

    private static void SaveRefreshToken(string username, string token)
    {
        var vault = new PasswordVault(); try { vault.Remove(vault.Retrieve(VaultResource, username)); } catch { }
        vault.Add(new PasswordCredential(VaultResource, username, token));
    }
    private static string? LoadRefreshToken(string username) { try { var item = new PasswordVault().Retrieve(VaultResource, username); item.RetrievePassword(); return item.Password; } catch { return null; } }
    private static int GetAvailablePort() { var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static string Form(Dictionary<string, string> values) => string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed record TokenResponse(string AccessToken, string? RefreshToken);
}
