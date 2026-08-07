namespace ClipSync.Core.Net;

// ============================================================
// 服务器地址规范化
//
// 用户在设置里只需要填 `192.168.1.10:8080` 或 `example.com`，
// `ws://` 前缀由程序补齐。443 端口和 https 输入会归一到 `wss://`。
//
// 对应 Mac 端 ServerAddress.swift 与 Android 端 ServerAddress.kt，
// 三端行为保持一致。
// ============================================================
public static class ServerAddress
{
    /// <summary>
    /// 把用户输入补成完整的 WebSocket 地址。
    /// 空输入返回空串，交由调用方提示「请填写服务器地址」。
    /// </summary>
    public static string Normalize(string? raw)
    {
        var s = (raw ?? "").Trim().TrimEnd('/');
        if (s.Length == 0) return "";

        if (s.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }
        // http/https 是常见误填，直接映射到对应的 WebSocket scheme
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "wss://" + s["https://".Length..];
        }
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "ws://" + s["http://".Length..];
        }
        // 443 端口默认按 TLS 处理，省得用户再手填 wss
        if (s.EndsWith(":443", StringComparison.Ordinal)) return "wss://" + s;
        return "ws://" + s;
    }

    /// <summary>界面展示用：去掉 scheme，输入框里就不必出现 ws:// 了。</summary>
    public static string DisplayForm(string? raw)
    {
        var s = (raw ?? "").Trim();
        foreach (var prefix in new[] { "wss://", "ws://", "https://", "http://" })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..];
                break;
            }
        }
        return s.TrimEnd('/');
    }

    /// <summary>
    /// 把 ws:// / wss:// 的服务器地址转成 http:// / https:// 的 REST 基址。
    /// 设置页里填的是 WebSocket 地址，认证接口走同一个端口的 HTTP。
    /// </summary>
    public static string HttpBase(string? serverUrl)
    {
        var s = (serverUrl ?? "").Trim().TrimEnd('/');
        if (s.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + s["wss://".Length..];
        }
        if (s.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + s["ws://".Length..];
        }
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }
        return "http://" + s;
    }
}
