using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MindAttic.Legion;

/// <summary>
/// Reads and refreshes the Claude Code CLI's OAuth credentials from
/// <c>~/.claude/.credentials.json</c>, so Legion can authenticate as the same
/// Team account without a separate Anthropic API key.
///
/// <para>If the access token is within 60 seconds of expiry (or already expired),
/// the source performs a synchronous token refresh using the stored refresh token
/// and writes the updated credentials back to the same file — keeping Claude Code
/// and Legion in sync.</para>
///
/// <para>Thread-safe: concurrent callers that all see an expired token will
/// block on a single refresh attempt; only the first caller actually hits the
/// network.</para>
/// </summary>
internal static class ClaudeCodeOAuthSource
{
    // Endpoint extracted from the Claude Code binary.
    private const string TokenEndpoint = "https://platform.claude.com/v1/oauth/token";

    // OAuth client_id used by the Claude Code CLI.
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    // Anthropic OAuth API version header required by the token endpoint.
    private const string OAuthVersion = "oauth-2025-04-20";

    // Access-token prefix — triggers Bearer auth instead of x-api-key.
    internal const string OAuthTokenPrefix = "sk-ant-oat";

    private static readonly object RefreshLock = new();

    private static string CredentialsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", ".credentials.json");

    /// <summary>
    /// Returns a valid OAuth access token, refreshing it if it is expired or
    /// about to expire. Returns <c>null</c> when the credentials file is absent,
    /// malformed, or the refresh call fails.
    /// </summary>
    public static string? GetAccessToken()
    {
        try
        {
            var (access, refresh, expiresAt) = ReadCredentials();
            if (access is null || refresh is null) return null;

            if (NotExpiredSoon(expiresAt)) return access;

            return PerformRefresh(refresh);
        }
        catch
        {
            return null;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool NotExpiredSoon(long expiresAtMs)
        => expiresAtMs > DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds();

    private static (string? Access, string? Refresh, long ExpiresAt) ReadCredentials()
    {
        if (!File.Exists(CredentialsPath)) return (null, null, 0);

        var json = File.ReadAllText(CredentialsPath);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            return (null, null, 0);

        var access  = oauth.TryGetProperty("accessToken",  out var at) ? at.GetString() : null;
        var refresh = oauth.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
        var expiry  = oauth.TryGetProperty("expiresAt",    out var ea) ? ea.GetInt64()  : 0L;

        return (access, refresh, expiry);
    }

    private static string? PerformRefresh(string refreshToken)
    {
        lock (RefreshLock)
        {
            // Another thread may have refreshed while we waited for the lock.
            var (existingAccess, _, existingExpiry) = ReadCredentials();
            if (NotExpiredSoon(existingExpiry)) return existingAccess;

            var payload = JsonSerializer.Serialize(new
            {
                grant_type    = "refresh_token",
                refresh_token = refreshToken,
                client_id     = ClientId,
            });

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var req  = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            req.Headers.Add("anthropic-version", OAuthVersion);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = http.Send(req);
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(body);

            var newAccess = doc.RootElement.TryGetProperty("access_token", out var at)
                ? at.GetString() : null;
            if (string.IsNullOrWhiteSpace(newAccess)) return null;

            var expiresIn   = doc.RootElement.TryGetProperty("expires_in", out var ei)
                ? ei.GetInt64() : 86400L;
            var newExpiry   = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds();
            var newRefresh  = doc.RootElement.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString() ?? refreshToken : refreshToken;

            PersistCredentials(newAccess, newRefresh, newExpiry);
            return newAccess;
        }
    }

    private static void PersistCredentials(string accessToken, string refreshToken, long expiresAt)
    {
        if (!File.Exists(CredentialsPath)) return;

        var raw  = File.ReadAllText(CredentialsPath);
        var root = JsonNode.Parse(raw);
        if (root is not JsonObject rootObj) return;

        if (rootObj["claudeAiOauth"] is not JsonObject oauth) return;

        oauth["accessToken"]  = accessToken;
        oauth["refreshToken"] = refreshToken;
        oauth["expiresAt"]    = expiresAt;

        var tmp = CredentialsPath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString());
        File.Move(tmp, CredentialsPath, overwrite: true);
    }
}
