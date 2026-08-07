using System.Text;

namespace ClipSync.Core.Crypto;

using ClipSync.Core.Diagnostics;
using ClipSync.Core.Protocol;

// ============================================================
// PayloadCipher：在「业务 payload」和「加密信封」之间来回转换
//
// 发送：MessagePayload(明文) → JSON → AES-GCM → MessagePayload(仅 enc + 占位 preview)
// 接收：MessagePayload(含 enc) → 解密 → MessagePayload(明文)
//
// 密钥按密码缓存，避免每条消息都跑一次 20 万轮 PBKDF2。缓存不需要失效逻辑
// ——密钥是密码的纯函数（盐写死在 E2EECrypto 里），只需限制条数。
// ============================================================
public static class PayloadCipher
{
    /// <summary>加密消息在 UI / 日志里的占位文案（不含任何真实内容）</summary>
    public const string Placeholder = "🔒 加密消息";

    /// <summary>缓存最多留几把（够覆盖"连接在用的"+"设置页正在试的"）</summary>
    private const int MaxCachedKeys = 4;

    private static readonly Dictionary<string, byte[]> KeyCache = new(StringComparer.Ordinal);
    private static readonly List<string> KeyOrder = new();
    private static readonly object Gate = new();

    /// <summary>取当前同步密码对应的密钥；密码为空返回 null（等于关闭加密）。</summary>
    public static byte[]? CurrentKey(string password)
    {
        if (string.IsNullOrEmpty(password)) return null;

        lock (Gate)
        {
            if (KeyCache.TryGetValue(password, out var cached))
            {
                Touch(password);
                return cached;
            }
        }

        // 派生放在锁外：单次要跑 20 万轮 PBKDF2，持锁会把正在发消息的线程一起
        // 堵住。并发算同一个密码最多白跑一次，无副作用。
        var key = E2EECrypto.DeriveKey(password);
        if (key is null) return null;

        lock (Gate)
        {
            KeyCache[password] = key;
            Touch(password);
            while (KeyOrder.Count > MaxCachedKeys)
            {
                KeyCache.Remove(KeyOrder[0]);
                KeyOrder.RemoveAt(0);
            }
        }
        return key;
    }

    /// <summary>把密码挪到使用顺序末尾。调用方必须已持锁。</summary>
    private static void Touch(string password)
    {
        KeyOrder.Remove(password);
        KeyOrder.Add(password);
    }

    /// <summary>清空密钥缓存（仅测试 / 排查用）。</summary>
    public static void InvalidateKeyCache()
    {
        lock (Gate)
        {
            KeyCache.Clear();
            KeyOrder.Clear();
        }
    }

    /// <summary>当前密钥指纹，用于设置页展示 / 排查两端密码不一致。</summary>
    public static string? Fingerprint(string password)
    {
        var key = CurrentKey(password);
        return key is null ? null : E2EECrypto.Fingerprint(key);
    }

    // MARK: - 发送方向

    /// <summary>
    /// 把明文 payload 封成密文 payload。
    /// 未设置同步密码时返回原文（服务端 e2ee.require=false 才允许）。
    /// </summary>
    public static MessagePayload Encrypt(MessagePayload payload, string password)
    {
        var key = CurrentKey(password);
        if (key is null) return payload;

        var plain = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(payload));
        var envelope = E2EECrypto.Seal(plain, key);
        if (envelope is null)
        {
            Log.Warn("[E2EE] 加密失败，退回明文发送");
            return payload;
        }

        // 只保留信封 + 占位预览；kind 保留以便收端在解密前做分类
        return new MessagePayload
        {
            Preview = Placeholder,
            Kind = payload.Kind,
            Enc = envelope,
        };
    }

    // MARK: - 接收方向

    public enum DecryptStatus
    {
        /// <summary>对端没加密</summary>
        Plaintext,
        /// <summary>解密成功</summary>
        Decrypted,
        /// <summary>密码不一致或数据损坏</summary>
        Failed,
    }

    /// <summary>
    /// 解密结果：区分"本来就是明文"、"解开了"、"解不开"三种情况，
    /// 让 UI 能给出准确提示而不是笼统地失败。
    /// </summary>
    public readonly record struct DecryptOutcome(
        DecryptStatus Status,
        MessagePayload? Payload,
        string? Fingerprint);

    public static DecryptOutcome Decrypt(MessagePayload payload, string password)
    {
        var envelope = payload.Enc;
        if (envelope is null)
        {
            return new DecryptOutcome(DecryptStatus.Plaintext, payload, null);
        }

        var key = CurrentKey(password);
        if (key is null)
        {
            return new DecryptOutcome(DecryptStatus.Failed, null, envelope.Fp);
        }

        var plain = E2EECrypto.Open(envelope, key);
        if (plain is null)
        {
            return new DecryptOutcome(DecryptStatus.Failed, null, envelope.Fp);
        }

        MessagePayload? decoded;
        try
        {
            decoded = ProtocolJson.Deserialize<MessagePayload>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex)
        {
            Log.Warn($"[E2EE] 解密后的 payload 解析失败: {ex.Message}");
            return new DecryptOutcome(DecryptStatus.Failed, null, envelope.Fp);
        }
        if (decoded is null)
        {
            return new DecryptOutcome(DecryptStatus.Failed, null, envelope.Fp);
        }

        decoded.Enc = null;
        return new DecryptOutcome(DecryptStatus.Decrypted, decoded, envelope.Fp);
    }
}
