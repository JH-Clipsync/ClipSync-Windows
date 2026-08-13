using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipSync.Core.Storage;

using ClipSync.Core.Crypto;
using ClipSync.Core.Diagnostics;

// ============================================================
// 设置存储：服务器地址、账号、同步密码、显示内容、自动同步剪贴板
//
// token 不需要手填，也没有单独的「登录」按钮：账号密码存在本地，
// 连接时自动换 token。账号由管理员在服务端创建。
// 同步密码（SyncPassword）是端到端加密的密钥来源，只留本机，从不上传。
//
// 落盘用 JSON（%APPDATA%\ClipSync\settings.json）而不是注册表：
// 便于用户直接查看/备份，也方便卸载时一并清理。
// ============================================================
public sealed class SettingsStore : INotifyPropertyChanged
{
    public static SettingsStore Shared { get; } = Load();

    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed class Snapshot
    {
        [JsonPropertyName("serverURL")] public string ServerUrl { get; set; } = "ws://localhost:8080";
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("password")] public string Password { get; set; } = "";
        [JsonPropertyName("token")] public string Token { get; set; } = "";
        [JsonPropertyName("syncPassword")] public string SyncPassword { get; set; } = "";
        [JsonPropertyName("e2eeEnabled")] public bool E2eeEnabled { get; set; } = true;
        [JsonPropertyName("showContent")] public bool ShowContent { get; set; } = true;
        [JsonPropertyName("autoSyncClipboard")] public bool AutoSyncClipboard { get; set; } = true;
        [JsonPropertyName("autoStart")] public bool AutoStart { get; set; }
        [JsonPropertyName("minimizeToTrayOnClose")] public bool MinimizeToTrayOnClose { get; set; } = true;
        [JsonPropertyName("onboardingCompleted")] public bool OnboardingCompleted { get; set; }
    }

    private readonly Snapshot _data;
    private readonly object _saveGate = new();
    private Timer? _saveTimer;

    private SettingsStore(Snapshot data) => _data = data;

    public string ServerUrl
    {
        get => _data.ServerUrl;
        set { if (_data.ServerUrl == value) return; _data.ServerUrl = value; Persist(); }
    }

    public string Username
    {
        get => _data.Username;
        set { if (_data.Username == value) return; _data.Username = value; Persist(); }
    }

    /// <summary>登录密码。连接时用它换 token，所以要持久化。</summary>
    public string Password
    {
        get => _data.Password;
        set
        {
            if (_data.Password == value) return;
            _data.Password = value;
            // 改了密码，本地 token 可能已经对不上账号，作废让它重新换
            if (!string.IsNullOrEmpty(value)) _data.Token = "";
            Persist();
            Notify(nameof(Token));
            Notify(nameof(IsLoggedIn));
            Notify(nameof(HasCredentials));
        }
    }

    /// <summary>服务端签发的 token（登录后自动写入）。</summary>
    public string Token
    {
        get => _data.Token;
        set
        {
            if (_data.Token == value) return;
            _data.Token = value;
            Persist();
            Notify(nameof(IsLoggedIn));
        }
    }

    /// <summary>端到端加密用的同步密码。只存本机，两端填一致才能互相解密。</summary>
    public string SyncPassword
    {
        get => _data.SyncPassword;
        set
        {
            if (_data.SyncPassword == value) return;
            _data.SyncPassword = value;
            // 密码变了：必须把旧密码派生出的密钥从 PayloadCipher 缓存里清掉，
            // 否则下一条发送/接收仍会命中旧缓存，导致"明明改了密码还在用旧密钥解密"
            PayloadCipher.InvalidateKeyCache();
            Persist();
            Notify(nameof(EffectiveSyncPassword));
            Notify(nameof(UsingBuiltinSyncPassword));
        }
    }

    /// <summary>是否启用端到端加密（关闭时发明文；服务端 e2ee.require=true 会拒收）。</summary>
    public bool E2eeEnabled
    {
        get => _data.E2eeEnabled;
        set
        {
            if (_data.E2eeEnabled == value) return;
            _data.E2eeEnabled = value;
            // 开关切换 = EffectiveSyncPassword 变了（空串 ↔ 密码），旧缓存必须作废
            PayloadCipher.InvalidateKeyCache();
            Persist();
            Notify(nameof(EffectiveSyncPassword));
            Notify(nameof(UsingBuiltinSyncPassword));
            Notify(nameof(EncryptionActive));
        }
    }

    /// <summary>true=弹窗显示消息内容；false=只显示占位。</summary>
    public bool ShowContent
    {
        get => _data.ShowContent;
        set { if (_data.ShowContent == value) return; _data.ShowContent = value; Persist(); }
    }

    /// <summary>true=本机剪贴板变化自动推送到服务端。</summary>
    public bool AutoSyncClipboard
    {
        get => _data.AutoSyncClipboard;
        set { if (_data.AutoSyncClipboard == value) return; _data.AutoSyncClipboard = value; Persist(); }
    }

    /// <summary>开机自启（写 HKCU Run 键，由 App 层落实）。</summary>
    public bool AutoStart
    {
        get => _data.AutoStart;
        set { if (_data.AutoStart == value) return; _data.AutoStart = value; Persist(); }
    }

    /// <summary>关闭主窗口时收进托盘而不是退出。</summary>
    public bool MinimizeToTrayOnClose
    {
        get => _data.MinimizeToTrayOnClose;
        set { if (_data.MinimizeToTrayOnClose == value) return; _data.MinimizeToTrayOnClose = value; Persist(); }
    }

    /// <summary>是否已完成首次启动向导。未完成时 App 启动会先弹 OnboardingWizard。</summary>
    public bool OnboardingCompleted
    {
        get => _data.OnboardingCompleted;
        set { if (_data.OnboardingCompleted == value) return; _data.OnboardingCompleted = value; Persist(); }
    }

    /// <summary>
    /// 实际用来派生密钥的密码。
    ///
    /// 开关关闭 → 空串（明文传输）；
    /// 开关打开但用户没填 → 内置默认密码，避免「开了加密却在发明文」；
    /// 开关打开且填了 → 用户自己的密码。
    /// </summary>
    public string EffectiveSyncPassword =>
        !E2eeEnabled ? "" : (SyncPassword.Length == 0 ? E2EECrypto.BuiltinSyncPassword : SyncPassword);

    /// <summary>当前是否在用内置默认密码（界面据此提示用户）。</summary>
    public bool UsingBuiltinSyncPassword => E2eeEnabled && SyncPassword.Length == 0;

    /// <summary>加密是否生效。开关打开就一定生效——没填密码时走内置默认密码。</summary>
    public bool EncryptionActive => E2eeEnabled;

    /// <summary>已登录 = 本地有 token。</summary>
    public bool IsLoggedIn => Token.Length > 0;

    /// <summary>账号密码都填了才能连接（连接时自动换 token）。</summary>
    public bool HasCredentials => Username.Length > 0 && Password.Length > 0;

    // MARK: - 持久化

    private static SettingsStore Load()
    {
        try
        {
            var path = AppPaths.SettingsFile;
            if (File.Exists(path))
            {
                var snapshot = JsonSerializer.Deserialize<Snapshot>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (snapshot is not null)
                {
                    // token 是会话级凭证：每次启动都必须用账号密码重新校验，
                    // 不能复用上次落盘的 token 直连。
                    snapshot.Token = "";
                    return new SettingsStore(snapshot);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[Settings] 读取失败，使用默认值: {ex.Message}");
        }
        return new SettingsStore(new Snapshot());
    }

    /// <summary>200ms 防抖后写盘：改开关时不必每次按键都落一次磁盘。</summary>
    private void Persist([CallerMemberName] string? propertyName = null)
    {
        if (propertyName is not null) Notify(propertyName);
        lock (_saveGate)
        {
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ => SaveNow(), null, 200, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        try
        {
            AppPaths.EnsureRoot();
            // 写盘前把 token 临时清空：token 是会话凭证，不应持久化，
            // 每次启动都必须用账号密码重新登录换取。
            var savedToken = _data.Token;
            _data.Token = "";
            var json = JsonSerializer.Serialize(
                _data, new JsonSerializerOptions { WriteIndented = true });
            _data.Token = savedToken;
            File.WriteAllText(AppPaths.SettingsFile, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Settings] 保存失败: {ex.Message}");
        }
    }

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
