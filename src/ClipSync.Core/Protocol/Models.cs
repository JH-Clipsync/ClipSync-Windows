using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipSync.Core.Protocol;

// ============================================================
// 线上协议模型。字段与另外三端逐字段对应：
//   服务端 e2ee.go / main.go、Mac 端 Models.swift、Android 端 Message.kt
// 任何字段改名都要四端同步，所以这里显式写 JsonPropertyName，
// 不依赖命名策略推导。
// ============================================================

/// <summary>端到端加密信封。对应服务端 EncEnvelope。</summary>
public sealed class EncEnvelope
{
    [JsonPropertyName("v")] public int V { get; set; }
    [JsonPropertyName("alg")] public string Alg { get; set; } = "";
    [JsonPropertyName("kdf")] public string Kdf { get; set; } = "";
    [JsonPropertyName("iter")] public int Iter { get; set; }
    [JsonPropertyName("salt")] public string Salt { get; set; } = "";
    [JsonPropertyName("iv")] public string Iv { get; set; } = "";
    [JsonPropertyName("ct")] public string Ct { get; set; } = "";
    [JsonPropertyName("fp")] public string Fp { get; set; } = "";
}

/// <summary>业务载荷。enc 非空时 text/data 为空，真实内容在 enc.ct 里。</summary>
public sealed class MessagePayload
{
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("mime")] public string? Mime { get; set; }
    /// <summary>base64（图片等二进制）</summary>
    [JsonPropertyName("data")] public string? Data { get; set; }
    [JsonPropertyName("preview")] public string? Preview { get; set; }
    /// <summary>业务子类型：sms_code / text / image / share</summary>
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    /// <summary>短信发件人，服务端清洗后填入</summary>
    [JsonPropertyName("sender")] public string? Sender { get; set; }
    [JsonPropertyName("enc")] public EncEnvelope? Enc { get; set; }

    public MessagePayload Clone() => new()
    {
        Text = Text, Mime = Mime, Data = Data,
        Preview = Preview, Kind = Kind, Sender = Sender, Enc = Enc,
    };
}

/// <summary>消息类型（推送通道），与服务端 TypeXxx 常量一致。</summary>
public static class MessageType
{
    public const string NotifyPC = "notify_pc";
    public const string NotifyMobile = "notify_mobile";
    public const string NotifyAll = "notify_all";
    public const string Clipboard = "clipboard";
    /// <summary>服务端主动踢下线（密码被管理员重置/账号禁用）。收到后立即断连并禁止自动重连。</summary>
    public const string ServerKick = "server_kick";
    /// <summary>在线设备列表变更（服务端在客户端上下线时下发）。</summary>
    public const string Presence = "presence";
}

/// <summary>在线设备（服务端 presence 消息里的一台设备）。</summary>
public sealed class OnlineDevice
{
    [JsonPropertyName("device_id")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("platform")] public string Platform { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("online_at")] public long OnlineAt { get; set; }
    [JsonPropertyName("self")] public bool IsSelf { get; set; }
    [JsonPropertyName("caps")] public Dictionary<string, bool> Caps { get; set; } = new();

    /// <summary>列表展示名：优先用户自定义名，否则平台标签。</summary>
    public string DisplayName
    {
        get
        {
            var n = (Name ?? "").Trim();
            return n.Length == 0 ? PlatformLabel : n;
        }
    }

    public string PlatformLabel => Platform?.ToLowerInvariant() switch
    {
        "mac" => "macOS",
        "windows" => "Windows",
        "linux" => "Linux",
        "android" => "Android",
        "ios" => "iOS",
        _ => Role == "mobile" ? "手机" : "电脑",
    };

    /// <summary>去掉前缀后的短设备 ID（取前 8 位）。</summary>
    public string ShortId
    {
        get
        {
            var id = DeviceId ?? "";
            var idx = id.IndexOf('-');
            var body = idx >= 0 ? id[(idx + 1)..] : id;
            return body.Length <= 8 ? body : body[..8];
        }
    }

    public bool ClipUp => Caps != null && Caps.TryGetValue("clip_up", out var v) && v;
    public bool SmsIn => Caps != null && Caps.TryGetValue("sms_in", out var v) && v;
    public bool AutoPut => Caps != null && Caps.TryGetValue("auto_put", out var v) && v;

    public DateTime OnlineTime =>
        (OnlineAt > 1_000_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(OnlineAt)
            : DateTimeOffset.FromUnixTimeSeconds(OnlineAt)).ToLocalTime().DateTime;
}

/// <summary>presence 消息的 payload。</summary>
public sealed class PresencePayload
{
    [JsonPropertyName("devices")] public List<OnlineDevice> Devices { get; set; } = new();
}

/// <summary>一次在线列表变化：新上线和刚下线的其他设备。</summary>
public sealed class PresenceChange
{
    public List<OnlineDevice> CameOnline { get; } = new();
    public List<OnlineDevice> WentOffline { get; } = new();
}

/// <summary>业务子类型（payload.kind）。</summary>
public static class MessageKind
{
    public const string SmsCode = "sms_code";
    public const string Text = "text";
    public const string Image = "image";
    public const string Share = "share";
}

/// <summary>业务大类，用于 UI 分组。三端约定值：sms / clipboard / notification。</summary>
public static class MessageCategory
{
    public const string Sms = "sms";
    public const string Clipboard = "clipboard";
    public const string Notification = "notification";

    /// <summary>与服务端 categorize() 保持一致的判定。</summary>
    public static string Of(string type, string? kind)
    {
        var k = kind ?? "";
        if (k.StartsWith("sms", StringComparison.Ordinal)) return Sms;
        if (type == MessageType.Clipboard
            || k == MessageKind.Text
            || k == MessageKind.Image
            || k == MessageKind.Share)
        {
            return Clipboard;
        }
        return Notification;
    }
}

/// <summary>内容格式。三端约定值：text / image。</summary>
public static class MessageContent
{
    public const string Text = "text";
    public const string Image = "image";

    /// <summary>与服务端 contentTypeOf() 保持一致的判定。</summary>
    public static string Of(string? kind, string? mime)
    {
        if (kind == Image) return Image;
        if (mime is not null && mime.StartsWith("image/", StringComparison.Ordinal)) return Image;
        return Text;
    }
}

/// <summary>统一消息结构。</summary>
public sealed class SyncMessage
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>notify_pc | notify_mobile | notify_all | clipboard</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonPropertyName("ts")] public long Ts { get; set; }
    [JsonPropertyName("payload")] public MessagePayload Payload { get; set; } = new();

    /// <summary>收到消息的本地时间。ts 大于 1e12 时按毫秒解释，否则按秒。</summary>
    [JsonIgnore]
    public DateTime Date =>
        (Ts > 1_000_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(Ts)
            : DateTimeOffset.FromUnixTimeSeconds(Ts)).ToLocalTime().DateTime;

    /// <summary>业务子类型，缺失时按 type 兜底（兼容旧格式）。</summary>
    [JsonIgnore]
    public string Kind
    {
        get
        {
            if (!string.IsNullOrEmpty(Payload.Kind)) return Payload.Kind!;
            if (Type == MessageType.Clipboard)
            {
                return Payload.Mime is not null
                       && Payload.Mime.StartsWith("image/", StringComparison.Ordinal)
                    ? MessageKind.Image
                    : MessageKind.Text;
            }
            return Type;
        }
    }

    [JsonIgnore] public string Category => MessageCategory.Of(Type, Payload.Kind);
    [JsonIgnore] public string Content => MessageContent.Of(Payload.Kind, Payload.Mime);
    [JsonIgnore] public bool IsClipboard => Category == MessageCategory.Clipboard;
    [JsonIgnore] public bool IsSms => Category == MessageCategory.Sms;

    /// <summary>是否是带 base64 数据的图片消息。</summary>
    [JsonIgnore]
    public bool HasImage => Content == MessageContent.Image && !string.IsNullOrEmpty(Payload.Data);

    public static long NowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>四端共用的 JSON 设置：不转义中文、忽略 null、字段名照抄协议。</summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // 服务端和另外两端都按 UTF-8 原样收发中文，转义成 \uXXXX 只会让日志难读
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
