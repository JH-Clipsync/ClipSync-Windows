using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipSync.Core.Net;

using ClipSync.Core.Protocol;

// ============================================================
// AuthClient：用用户名 + 密码换 token
//
// 服务端行为（auth_http.go）：
//   - 当前账号没有客户端在线 → 新签发 token
//   - 已有客户端在线 → 返回同一个 token（reused = true）
// 所以两端各自登录同一账号，就会自动落到同一个同步分组里。
// ============================================================

/// <summary>登录成功后的会话信息。</summary>
public sealed class AuthSession
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("user_id")] public long UserId { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("expires_at")] public string? ExpiresAt { get; set; }
    /// <summary>true = 复用了已在线客户端的 token</summary>
    [JsonPropertyName("reused")] public bool Reused { get; set; }
    /// <summary>服务端是否强制要求端到端加密</summary>
    [JsonPropertyName("e2ee_required")] public bool E2eeRequired { get; set; }
    [JsonPropertyName("online_devices")] public int OnlineDevices { get; set; }
}

public enum AuthFailureKind
{
    BadUrl,
    Network,
    Server,
    Decode,
}

public sealed class AuthException : Exception
{
    public AuthFailureKind Kind { get; }
    public int StatusCode { get; }

    public AuthException(AuthFailureKind kind, string message, int statusCode = 0)
        : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    /// <summary>把登录异常翻成一句用户能照着处理的话。</summary>
    public static string Describe(Exception error)
    {
        if (error is not AuthException auth)
        {
            return $"连接失败：{error.Message}";
        }
        return auth.Kind switch
        {
            AuthFailureKind.BadUrl => $"服务器地址不合法：{auth.Message}",
            AuthFailureKind.Network =>
                $"连不上服务器（{auth.Message}），请检查地址、网络和服务是否已启动",
            AuthFailureKind.Server when auth.StatusCode is 401 or 403 =>
                $"登录失败：{auth.Message}，请检查用户名和密码",
            AuthFailureKind.Server => $"服务端拒绝登录：{auth.Message}",
            AuthFailureKind.Decode => $"服务端响应异常：{auth.Message}",
            _ => $"连接失败：{auth.Message}",
        };
    }
}

public sealed class AuthClient
{
    public static AuthClient Shared { get; } = new();

    private readonly HttpClient _http;

    public AuthClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>POST /auth/login</summary>
    public async Task<AuthSession> LoginAsync(
        string server, string username, string password, CancellationToken ct = default)
    {
        var json = await PostAsync(
            server, "/auth/login",
            new Dictionary<string, string> { ["username"] = username, ["password"] = password },
            token: null, ct).ConfigureAwait(false);

        var session = Materialize(json, username);
        if (string.IsNullOrEmpty(session.Token))
        {
            throw new AuthException(AuthFailureKind.Decode, "响应缺少 token");
        }
        return session;
    }

    /// <summary>GET /auth/session —— 启动时用它确认本地 token 还有效。</summary>
    public async Task<bool> CheckSessionAsync(
        string server, string token, CancellationToken ct = default)
    {
        var url = ServerAddress.HttpBase(server) + "/auth/session";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new AuthException(AuthFailureKind.BadUrl, server);
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is not AuthException)
        {
            throw new AuthException(AuthFailureKind.Network, ex.Message);
        }
    }

    /// <summary>POST /auth/logout —— 作废服务端会话。</summary>
    public async Task LogoutAsync(string server, string token, CancellationToken ct = default)
    {
        await PostAsync(server, "/auth/logout",
            new Dictionary<string, string>(), token, ct).ConfigureAwait(false);
    }

    // MARK: - 内部

    private static AuthSession Materialize(JsonElement json, string fallbackUsername)
    {
        var session = new AuthSession { Username = fallbackUsername };
        if (json.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
        {
            session.Token = t.GetString() ?? "";
        }
        if (json.TryGetProperty("user_id", out var uid) && uid.TryGetInt64(out var uidValue))
        {
            session.UserId = uidValue;
        }
        if (json.TryGetProperty("username", out var name) && name.ValueKind == JsonValueKind.String)
        {
            session.Username = name.GetString() ?? fallbackUsername;
        }
        if (json.TryGetProperty("expires_at", out var exp) && exp.ValueKind == JsonValueKind.String)
        {
            session.ExpiresAt = exp.GetString();
        }
        if (json.TryGetProperty("reused", out var reused)
            && reused.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            session.Reused = reused.GetBoolean();
        }
        if (json.TryGetProperty("e2ee_required", out var req)
            && req.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            session.E2eeRequired = req.GetBoolean();
        }
        if (json.TryGetProperty("online_devices", out var dev) && dev.TryGetInt32(out var devValue))
        {
            session.OnlineDevices = devValue;
        }
        return session;
    }

    private async Task<JsonElement> PostAsync(
        string server, string path, Dictionary<string, string> body,
        string? token, CancellationToken ct)
    {
        var url = ServerAddress.HttpBase(server) + path;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new AuthException(AuthFailureKind.BadUrl, server);
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(
                ProtocolJson.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (token is not null)
        {
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        int status;
        string raw;
        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            status = (int)resp.StatusCode;
            raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not AuthException)
        {
            throw new AuthException(AuthFailureKind.Network, ex.Message);
        }

        JsonElement json;
        try
        {
            json = string.IsNullOrWhiteSpace(raw)
                ? default
                : JsonDocument.Parse(raw).RootElement.Clone();
        }
        catch
        {
            json = default;
        }

        if (status != (int)HttpStatusCode.OK)
        {
            var msg = json.ValueKind == JsonValueKind.Object
                      && json.TryGetProperty("error", out var err)
                      && err.ValueKind == JsonValueKind.String
                ? err.GetString() ?? $"服务端返回 {status}"
                : $"服务端返回 {status}";
            throw new AuthException(AuthFailureKind.Server, msg, status);
        }

        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new AuthException(AuthFailureKind.Decode, "响应不是合法 JSON 对象");
        }
        return json;
    }
}
