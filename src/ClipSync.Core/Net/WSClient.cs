using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;

namespace ClipSync.Core.Net;

using ClipSync.Core.Crypto;
using ClipSync.Core.Diagnostics;
using ClipSync.Core.Protocol;
using ClipSync.Core.Storage;

// ============================================================
// WSClient：与服务器的 WebSocket 长连接
// - 连接 / 断开 / 心跳 ping / token 失效自动重新鉴权
// - 收消息 → 解密 → 抛 MessageReceived 事件
// - State 通过 PropertyChanged 暴露给 UI
//
// 行为对齐 Mac 端 WSClient.swift：连接失败后不自动无限重连，
// 而是停下来把原因摆到界面上，等用户点「重连」。
// ============================================================
public sealed class WSClient : INotifyPropertyChanged
{
    public static WSClient Shared { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>收到一条（已解密的）消息。</summary>
    public event Action<SyncMessage>? MessageReceived;

    private ConnectionState _state = ConnectionState.Disconnected;
    private string? _authError;
    private string? _decryptFailure;
    private SyncMessage? _lastMessage;

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            Notify(nameof(State));
        }
    }

    /// <summary>最近一次连接失败的原因，UI 直接展示给用户。</summary>
    public string? AuthError
    {
        get => _authError;
        private set
        {
            if (_authError == value) return;
            _authError = value;
            Notify(nameof(AuthError));
        }
    }

    /// <summary>收到了解不开的密文（两端同步密码不一致）时置位。</summary>
    public string? DecryptFailure
    {
        get => _decryptFailure;
        private set
        {
            if (_decryptFailure == value) return;
            _decryptFailure = value;
            Notify(nameof(DecryptFailure));
        }
    }

    /// <summary>最近收到的一条消息（UI 绑定 Toast 用）。</summary>
    public SyncMessage? LastMessage
    {
        get => _lastMessage;
        private set
        {
            // 关键：即使内容相同，也要强制触发变更。
            // 用"先设 null 再赋值"绕过 INotifyPropertyChanged 的值相等去重，
            // 否则同一手机号的连续验证码，第二条不会弹通知。
            if (value is not null)
            {
                _lastMessage = null;
                Notify(nameof(LastMessage));
            }
            _lastMessage = value;
            Notify(nameof(LastMessage));
        }
    }

    /// <summary>本机设备 ID：首次生成后存进 Settings，之后一直复用。
    /// 不能每次启动都换：服务端按 device_id 在 Redis 里登记在线设备。</summary>
    public string DeviceId { get; }

    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _lifecycleGate = new();

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _pingLoop;
    private string _currentServer = "";
    private string _currentToken = "";
    private bool _isRunning;

    /// <summary>最短 "连接中" 显示时间：进入 connecting 后至少停留 1.5s。</summary>
    private DateTime? _connectingSince;
    private static readonly TimeSpan MinConnectingDuration = TimeSpan.FromSeconds(1.5);

    /// <summary>把状态回调投到 UI 线程。App 启动时注入 Dispatcher.Invoke。</summary>
    private Action<Action> _dispatch = action => action();

    public void UseDispatcher(Action<Action> dispatch) => _dispatch = dispatch;

    private WSClient()
    {
        // 从存储读取或生成新的 deviceId，确保同一设备每次启动 ID 不变
        DeviceId = LoadOrCreateDeviceId();
    }

    private static string LoadOrCreateDeviceId()
    {
        try
        {
            var path = Path.Combine(AppPaths.Root, "device.id");
            if (File.Exists(path))
            {
                var saved = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(saved)) return saved;
            }
            AppPaths.EnsureRoot();
            var fresh = "win-" + Guid.NewGuid().ToString("N")[..8];
            File.WriteAllText(path, fresh);
            return fresh;
        }
        catch
        {
            return "win-" + Guid.NewGuid().ToString("N")[..8];
        }
    }

    // MARK: - 连接控制

    /// <summary>
    /// 统一的连接入口：没有 token 就先用账号密码换一个，再建立 WebSocket。
    ///
    /// 界面上因此只需要一个「连接」按钮。只在本地没有 token 时才打 /auth/login，
    /// 避免每次重连都去撞登录限流；token 失效由 WS 握手的 401 触发重新换取。
    /// </summary>
    public async Task ConnectAsync(SettingsStore settings, CancellationToken ct = default)
    {
        var server = ServerAddress.Normalize(settings.ServerUrl);
        if (server.Length == 0)
        {
            SetAuthError("请先填写服务器地址，例如 192.168.1.10:8080");
            return;
        }

        if (settings.Token.Length > 0)
        {
            Start(server, settings.Token, settings);
            return;
        }

        if (!settings.HasCredentials)
        {
            SetAuthError(settings.Username.Length == 0
                ? "请填写用户名（账号由管理员创建）"
                : "请填写密码");
            return;
        }

        _dispatch(() =>
        {
            State = ConnectionState.Connecting;
            AuthError = null;
        });

        try
        {
            var session = await AuthClient.Shared
                .LoginAsync(server, settings.Username, settings.Password, ct)
                .ConfigureAwait(false);
            settings.Token = session.Token;
            settings.Username = session.Username;
            Log.Info($"[WS] 连接前自动登录成功：reused={session.Reused} 在线 {session.OnlineDevices} 台");
            Start(server, session.Token, settings);
        }
        catch (Exception ex)
        {
            _dispatch(() =>
            {
                State = ConnectionState.Disconnected;
                AuthError = AuthException.Describe(ex);
            });
            Log.Warn($"[WS] 自动登录失败: {ex.Message}");
        }
    }

    /// <summary>token 失效后重新换一个并接着连。密码存在本地，用户无需干预。</summary>
    public async Task ReauthenticateAsync(SettingsStore settings, CancellationToken ct = default)
    {
        settings.Token = "";
        await ConnectAsync(settings, ct).ConfigureAwait(false);
    }

    /// <summary>启动连接。相同 server + token 且已在运行时直接跳过（防抖）。</summary>
    public void Start(string server, string token, SettingsStore settings)
    {
        lock (_lifecycleGate)
        {
            if (_isRunning && server == _currentServer && token == _currentToken)
            {
                Log.Info("[WS] start 跳过：已在连接同一 server");
                return;
            }
        }

        StopInternal();

        if (token.Length == 0)
        {
            Log.Warn("[WS] start 失败：token 为空");
            return;
        }

        var url = $"{server}/ws?token={Uri.EscapeDataString(token)}" +
                  $"&device={Uri.EscapeDataString(DeviceId)}&role=pc";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            SetAuthError($"服务器地址不合法：{server}");
            return;
        }

        Log.Info($"[WS] 正在连接 {server}/ws?role=pc&device={DeviceId}");

        var cts = new CancellationTokenSource();
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        lock (_lifecycleGate)
        {
            _isRunning = true;
            _currentServer = server;
            _currentToken = token;
            _cts = cts;
            _socket = socket;
            _connectingSince = DateTime.Now;
        }

        _dispatch(() =>
        {
            State = ConnectionState.Connecting;
            AuthError = null;
        });

        _receiveLoop = Task.Run(() => RunAsync(socket, uri, settings, cts.Token));
        WarmUpEncryptionKey(settings);
    }

    /// <summary>预热加密密钥。</summary>
    private static void WarmUpEncryptionKey(SettingsStore settings)
    {
        var password = settings.EffectiveSyncPassword;
        if (password.Length == 0) return;
        Task.Run(() =>
        {
            PayloadCipher.CurrentKey(password);
            Log.Info("[WS] 端到端加密密钥已就绪");
        });
    }

    /// <summary>用户主动断开：连失败提示一起清掉，"手动断开"不是错误。</summary>
    public void Disconnect()
    {
        StopInternal();
        _dispatch(() =>
        {
            AuthError = null;
            DecryptFailure = null;
        });
    }

    private void StopInternal()
    {
        CancellationTokenSource? cts;
        ClientWebSocket? socket;
        lock (_lifecycleGate)
        {
            _isRunning = false;
            _currentServer = "";
            _currentToken = "";
            cts = _cts;
            socket = _socket;
            _cts = null;
            _socket = null;
        }

        try { cts?.Cancel(); } catch { /* 已释放 */ }
        try { socket?.Abort(); } catch { /* 已关闭 */ }
        socket?.Dispose();
        cts?.Dispose();

        _dispatch(() => State = ConnectionState.Disconnected);
    }

    private void SetAuthError(string message) => _dispatch(() =>
    {
        State = ConnectionState.Disconnected;
        AuthError = message;
    });

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // ============================================================
    // 发送
    // ============================================================

    /// <summary>发送剪贴板文本。</summary>
    public void SendClipboardText(string text)
    {
        var payload = new MessagePayload
        {
            Text = text,
            Mime = "text/plain",
            Preview = text.Length > 50 ? text[..50] : text,
            Kind = MessageKind.Text,
        };
        Send(new SyncMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = MessageType.Clipboard,
            From = DeviceId,
            To = "*",
            Ts = SyncMessage.NowMilliseconds(),
            Payload = payload,
        });
    }

    /// <summary>发送剪贴板图片（base64 + mime）。</summary>
    public void SendClipboardImage(string base64, string mime = "image/png")
    {
        var payload = new MessagePayload
        {
            Mime = mime,
            Data = base64,
            Preview = "[图片]",
            Kind = MessageKind.Image,
        };
        Send(new SyncMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = MessageType.Clipboard,
            From = DeviceId,
            To = "*",
            Ts = SyncMessage.NowMilliseconds(),
            Payload = payload,
        });
    }

    private void Send(SyncMessage msg)
    {
        // 按需加密：settings.EffectiveSyncPassword 非空时把 payload 换成信封
        var settings = SettingsStore.Shared;
        var password = settings.EffectiveSyncPassword;
        var outgoing = msg;
        if (password.Length > 0)
        {
            var encrypted = PayloadCipher.Encrypt(msg.Payload, password);
            outgoing = new SyncMessage
            {
                Id = msg.Id,
                Type = msg.Type,
                From = msg.From,
                To = msg.To,
                Ts = msg.Ts,
                Payload = encrypted,
            };
        }

        var json = ProtocolJson.Serialize(outgoing);
        Task.Run(async () =>
        {
            try
            {
                await _sendGate.WaitAsync().ConfigureAwait(false);
                ClientWebSocket? socket;
                lock (_lifecycleGate) { socket = _socket; }
                if (socket is null || socket.State != WebSocketState.Open)
                {
                    Log.Warn("[WS] 发送失败：WebSocket 未连接");
                    return;
                }
                var bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None).ConfigureAwait(false);
                Log.Info($"[WS] ↑ 已发送 {outgoing.Type}{(password.Length > 0 ? " (已加密)" : "")}");
            }
            catch (Exception ex)
            {
                Log.Warn($"[WS] 发送失败: {ex.Message}");
            }
            finally
            {
                _sendGate.Release();
            }
        });
    }

    // ============================================================
    // 接收循环 + 心跳
    // ============================================================

    private async Task RunAsync(
        ClientWebSocket socket,
        Uri uri,
        SettingsStore settings,
        CancellationToken ct)
    {
        // 启动心跳
        _pingLoop = PingLoopAsync(socket, settings, ct);

        try
        {
            await socket.ConnectAsync(uri, ct).ConfigureAwait(false);
            Log.Info("[WS] WebSocket 握手完成，进入接收循环");

            var buffer = new byte[1024 * 1024]; // 1MB 单条上限（服务端 readLimit 默认 10MB）
            var ms = new MemoryStream();

            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                ms.Position = 0;
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    var seg = new ArraySegment<byte>(buffer);
                    result = await socket.ReceiveAsync(seg, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                            .ConfigureAwait(false);
                        Log.Info($"[WS] 服务端关闭连接：{result.CloseStatusDescription}");
                        NoteAuthFailureIfNeeded(result.CloseStatusDescription ?? "", settings);
                        ScheduleReconnect(DescribeSocketFailure("服务端主动关闭"));
                        return;
                    }
                    if (result.Count > 0)
                    {
                        ms.Write(buffer, 0, result.Count);
                    }
                } while (!result.EndOfMessage);

                if (ms.Length > 0)
                {
                    var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                    Handle(text, settings);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消（用户主动断开），不提示错误
        }
        catch (Exception ex)
        {
            Log.Warn($"[WS] 接收错误: {ex.Message}");
            NoteAuthFailureIfNeeded(ex.Message, settings);
            ScheduleReconnect(DescribeSocketFailure(ex.Message));
        }
        finally
        {
            try { _pingLoop = null; } catch { }
        }
    }

    /// <summary>心跳：每 20 秒发一次 ping 文本，保证连接活着；第一次成功后置 connected。</summary>
    private async Task PingLoopAsync(
        ClientWebSocket socket,
        SettingsStore settings,
        CancellationToken ct)
    {
        var firstPing = true;
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                // 立即 ping 一次
                var pingBytes = Encoding.UTF8.GetBytes(
                    ProtocolJson.Serialize(new Dictionary<string, object> { ["type"] = "ping", ["ts"] = SyncMessage.NowMilliseconds() }));
                try
                {
                    await socket.SendAsync(
                        new ArraySegment<byte>(pingBytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true, ct).ConfigureAwait(false);

                    if (firstPing)
                    {
                        firstPing = false;
                        _dispatch(() =>
                        {
                            if (State != ConnectionState.Connected)
                            {
                                State = ConnectionState.Connected;
                                _connectingSince = null;
                                AuthError = null;
                                Log.Info("[WS] 已连接服务器");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"[WS] ping 失败: {ex.Message}");
                    if (socket.State != WebSocketState.Open) break;
                }

                await Task.Delay(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>处理一条收到的原始 JSON 文本。</summary>
    private void Handle(string text, SettingsStore settings)
    {
        var msg = ProtocolJson.Deserialize<SyncMessage>(text);
        if (msg is null)
        {
            Log.Warn($"[WS] 解析失败: {Truncate(text, 200)}");
            return;
        }

        // 过滤自己发的消息
        if (msg.From == DeviceId)
        {
            Log.Info($"[WS] ⏭ 跳过本人消息 from={msg.From}");
            return;
        }

        // 解密
        var outcome = PayloadCipher.Decrypt(msg.Payload, settings.EffectiveSyncPassword);
        SyncMessage resolved;
        switch (outcome.Status)
        {
            case PayloadCipher.DecryptStatus.Plaintext:
                Log.Info($"[WS] ↓ 收到 {msg.Type} text={Truncate(msg.Payload.Text ?? "", 60)}");
                resolved = msg;
                break;
            case PayloadCipher.DecryptStatus.Decrypted:
                resolved = new SyncMessage
                {
                    Id = msg.Id,
                    Type = msg.Type,
                    From = msg.From,
                    To = msg.To,
                    Ts = msg.Ts,
                    Payload = outcome.Payload!,
                };
                Log.Info($"[WS] ↓ 收到 {msg.Type} (已解密) text={Truncate(outcome.Payload!.Text ?? "", 60)}");
                break;
            case PayloadCipher.DecryptStatus.Failed:
            default:
                var remoteFp = outcome.Fingerprint ?? "未知";
                var localFp = PayloadCipher.Fingerprint(settings.EffectiveSyncPassword) ?? "未设置";
                Log.Warn($"[WS] 解密失败 对端 key={remoteFp} 本机 key={localFp}");
                _dispatch(() =>
                {
                    State = ConnectionState.Connected;
                    DecryptFailure = settings.E2eeEnabled
                        ? "收到无法解密的消息：请确认两端「同步密码」填写一致"
                        : "收到加密消息但本机未开启端到端加密，请在设置里打开";
                });
                return;
        }

        _dispatch(() =>
        {
            State = ConnectionState.Connected;
            DecryptFailure = null;
            LastMessage = resolved;
            MessageReceived?.Invoke(resolved);
        });
    }

    /// <summary>token 失效时尝试自动重新鉴权（握手错误含 401/Unauthorized）。</summary>
    private void NoteAuthFailureIfNeeded(string description, SettingsStore settings)
    {
        var looksLikeAuth = (description ?? "").Contains("401")
                           || (description ?? "").Contains("Unauthorized")
                           || (description ?? "").Contains("403");
        if (!looksLikeAuth) return;

        _dispatch(async () =>
        {
            Log.Info("[WS] token 已失效，尝试用已保存的账号密码重新换取");
            if (!settings.HasCredentials)
            {
                AuthError = "登录已失效，请到设置里填写账号密码";
                return;
            }
            StopInternal();
            await ReauthenticateAsync(settings).ConfigureAwait(false);
        });
    }

    private static string DescribeSocketFailure(string message) =>
        $"连接中断：{message}";

    /// <summary>停止连接并把断线原因摆到界面上（不自动重连）。</summary>
    private void ScheduleReconnect(string reason)
    {
        // 保证 "连接中" 至少显示 minConnectingDuration
        var delay = TimeSpan.Zero;
        if (_connectingSince.HasValue)
        {
            var elapsed = DateTime.Now - _connectingSince.Value;
            if (elapsed < MinConnectingDuration) delay = MinConnectingDuration - elapsed;
        }

        Task.Run(async () =>
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay).ConfigureAwait(false);
            lock (_lifecycleGate)
            {
                if (!_isRunning) return;
            }
            StopInternal();
            if (!string.IsNullOrEmpty(reason))
            {
                _dispatch(() => { if (AuthError is null) AuthError = reason; });
            }
            Log.Info("[WS] 连接失败，已停止（不自动重连；点重连按钮再试）");
        });
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}
