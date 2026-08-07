using System.Security.Cryptography;
using System.Text;

namespace ClipSync.Core.Crypto;

using ClipSync.Core.Protocol;

// ============================================================
// 端到端隧道加密。
//
// 密钥来自「用户在本机设置的同步密码」，服务端从不接触密码或密钥。
//
// 参数必须与另外三端逐字节一致：
//  - 派生：PBKDF2-HMAC-SHA256(password, salt, 200000) → 32 字节
//  - salt：SHA-256("clipsync-e2ee-v1")，各端写死同一个值，
//          于是"同一个密码"在任何设备上派生出同一把密钥，无需交换材料
//  - 加密：AES-256-GCM，12 字节随机 IV，16 字节 tag 附在密文尾部
//  - 指纹：SHA-256(key) 的前 16 个 hex 字符
//
// 对应实现：Mac 端 E2EECrypto.swift、Android 端 E2EECrypto.kt，
// 服务端只校验信封格式（e2ee.go）。
// ============================================================
public static class E2EECrypto
{
    public const int Version = 1;
    public const string Algorithm = "AES-256-GCM";
    public const string KdfName = "PBKDF2-HMAC-SHA256";
    public const int Iterations = 200_000;

    /// <summary>
    /// 内置默认同步密码：用户开了加密但没填自己的密码时使用。
    ///
    /// 写死在各端，所以"开了加密却没填密码"不会退化成明文，两端也不需要任何
    /// 约定就能互通。强度弱于用户自设密码（值是公开的），只作兜底默认值。
    /// 各端必须同步：Mac 端 E2EECrypto.builtinSyncPassword、
    /// Android 端 E2EECrypto.BUILTIN_SYNC_PASSWORD、服务端 BuiltinSyncPassword。
    /// </summary>
    public const string BuiltinSyncPassword = "cs1-louuMZxNFCXgL1AcXjlBCly2E54NeH5T";

    private const string SaltSeed = "clipsync-e2ee-v1";
    private const int IvLength = 12;
    private const int TagLength = 16;
    private const int KeyLength = 32;

    /// <summary>固定盐：SHA-256(SaltSeed)，32 字节。改动它会让所有历史密文无法解开。</summary>
    public static byte[] Salt { get; } = SHA256.HashData(Encoding.UTF8.GetBytes(SaltSeed));

    /// <summary>用同步密码派生 AES-256 密钥；密码为空返回 null（等于关闭加密）。</summary>
    public static byte[]? DeriveKey(string password)
    {
        if (string.IsNullOrEmpty(password)) return null;
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                password: Encoding.UTF8.GetBytes(password),
                salt: Salt,
                iterations: Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: KeyLength);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"[E2EE] 密钥派生失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>密钥指纹：SHA-256(key) 前 16 位 hex，用于提示两端密码是否一致。</summary>
    public static string Fingerprint(byte[] key) =>
        Convert.ToHexString(SHA256.HashData(key)).ToLowerInvariant()[..16];

    /// <summary>加密明文，返回可直接放进 payload.enc 的信封。</summary>
    public static EncEnvelope? Seal(byte[] plaintext, byte[] key)
    {
        try
        {
            var iv = RandomNumberGenerator.GetBytes(IvLength);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagLength];

            using var gcm = new AesGcm(key, TagLength);
            gcm.Encrypt(iv, plaintext, ciphertext, tag);

            // 另外两端的 GCM 输出都是「密文 + tag」连在一起，这里拼成同一布局
            var ciphertextAndTag = new byte[ciphertext.Length + tag.Length];
            ciphertext.CopyTo(ciphertextAndTag, 0);
            tag.CopyTo(ciphertextAndTag, ciphertext.Length);

            return new EncEnvelope
            {
                V = Version,
                Alg = Algorithm,
                Kdf = KdfName,
                Iter = Iterations,
                Salt = Convert.ToBase64String(Salt),
                Iv = Convert.ToBase64String(iv),
                Ct = Convert.ToBase64String(ciphertextAndTag),
                Fp = Fingerprint(key),
            };
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"[E2EE] 加密失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>解开信封。密码不对时 GCM 校验失败 → 返回 null。</summary>
    public static byte[]? Open(EncEnvelope envelope, byte[] key)
    {
        if (envelope.V != Version || envelope.Alg != Algorithm)
        {
            Diagnostics.Log.Warn($"[E2EE] 信封格式不支持 v={envelope.V} alg={envelope.Alg}");
            return null;
        }
        try
        {
            var iv = Convert.FromBase64String(envelope.Iv);
            var ciphertextAndTag = Convert.FromBase64String(envelope.Ct);
            if (ciphertextAndTag.Length < TagLength) return null;

            var cipherLength = ciphertextAndTag.Length - TagLength;
            var ciphertext = ciphertextAndTag.AsSpan(0, cipherLength);
            var tag = ciphertextAndTag.AsSpan(cipherLength, TagLength);
            var plaintext = new byte[cipherLength];

            using var gcm = new AesGcm(key, TagLength);
            gcm.Decrypt(iv, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (Exception ex)
        {
            // 最常见原因就是两端同步密码不一致
            Diagnostics.Log.Warn($"[E2EE] 解密失败（密码可能不一致）: {ex.Message}");
            return null;
        }
    }
}
