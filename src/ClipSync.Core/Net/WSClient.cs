using System.ComponentModel;
using System.Net.WebSockets;
using System.Net.Sockets;
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
// - 断线自动重连（指数退避：2, 4, 8, 16, 30, 30…），对齐 Mac 端 WSClient.swift
// - 用户主动断开 / 被服务端踢下线 → 不自动重连
// - token 失效重试仅限 1 次：第二次仍失败说明密码已被管理员重置
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
    private Timer? _reconnectTimer;
    private string _currentServer = "";
    private string _currentToken = "";
    private bool _isRunning;

    /// <summary>用户主动断开时置 true，ScheduleReconnect 据此跳过自动重连</summary>
    private bool _userInitiatedDisconnect;
    /// <summary>被服务端踢下线（密码重置/封禁）时置 true，阻止自动重连</summary>
    private bool _wasKicked;
    /// <summary>token 失效后重新登录的次数，超过 1 次不再重试（密码已被改）</summary>
    private int _authRetryCount;
    /// <summary>自动重连尝试次数（指数退避：2, 4, 8, 16, 30, 30…）</summary>
    private int _reconnectAttempts;
    private const int MaxReconnectDelaySeconds = 30;

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
    /// 统一的连接入口：每次都用账号密码调用 /auth/login 换取新 token，再建立 WebSocket。
    ///
    /// 安全要求：每次连接都必须校验账号密码，不允许复用本地缓存的 token 直连，
    /// 这样服务端可以实时校验密码是否被管理员重置/禁用。
    /// </summary>
    public async Task ConnectAsync(SettingsStore settings, CancellationToken ct = default)
    {
        var server = ServerAddress.Normalize(settings.ServerUrl);
        if (server.Length == 0)
        {
            SetAuthError("请先填写服务器地址，例如 192.168.1.10:8080");
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
            // 登录 HTTP 请求在 ConfigureAwait(false) 后续跑在线程池线程上，
            // 而 settings.Token/Username 的 setter 会触发 PropertyChanged，
            // UI 层的监听器会直接访问控件，必须回到 UI 线程执行。
            var token = session.Token;
            var username = session.Username;
            var reused = session.Reused;
            var online = session.OnlineDevices;
            _dispatch(() =>
            {
                settings.Token = token;
                settings.Username = username;
                Log.Info($"[WS] 连接前登录成功：reused={reused} 在线 {online} 台");
                Start(server, token, settings);
            });
        }
        catch (Exception ex)
        {
            _dispatch(() =>
            {
                State = ConnectionState.Disconnected;
                AuthError = DescribeLoginFailure(ex);
            });
            Log.Warn($"[WS] 登录失败: {ex.Message}");
        }
    }

    /// <summary>token 失效后重新换一个并接着连。密码存在本地，用户无需干预。
    ///
    /// 这里不复用 ConnectAsync，因为 ConnectAsync 的登录失败统一走 DescribeLoginFailure，
    /// 会把"密码已被管理端重置"也提示成"请检查用户名和密码"。重新登录失败要单独区分：
    /// 服务端拒绝（401/403）= 密码已失效；其他 = 网络问题。</summary>
    public async Task ReauthenticateAsync(SettingsStore settings, CancellationToken ct = default)
    {
        var server = ServerAddress.Normalize(settings.ServerUrl);
        if (server.Length == 0) return;

        settings.Token = "";
        _dispatch(() =>
        {
            State = ConnectionState.Connecting;
            AuthError = null;
            _connectingSince = DateTime.Now;
        });

        try
        {
            var session = await AuthClient.Shared
                .LoginAsync(server, settings.Username, settings.Password, ct)
                .ConfigureAwait(false);
            // 同 ConnectAsync：await 后回到 UI 线程再改 settings、调 Start
            var token = session.Token;
            _dispatch(() =>
            {
                settings.Token = token;
                Log.Info("[WS] 重新登录成功，继续连接");
                Start(server, token, settings);
            });
        }
        catch (AuthException aex)
        {
            _dispatch(() =>
            {
                State = ConnectionState.Disconnected;
                _connectingSince = null;
            });
            if (aex.StatusCode == 401 || aex.StatusCode == 403)
            {
                // 服务端不认这套账密 → 密码已被管理端重置
                _wasKicked = true;
                SetAuthError("密码已失效，请重新登录");
                Log.Warn($"[WS] 重新登录被拒（{aex.StatusCode}），密码已变更，不再重连");
            }
            else
            {
                SetAuthError(DescribeLoginFailure(aex));
                Log.Warn($"[WS] 重新登录被拒（{aex.StatusCode}），不再重连");
            }
        }
        catch (Exception ex)
        {
            _dispatch(() =>
            {
                State = ConnectionState.Disconnected;
                _connectingSince = null;
            });
            SetAuthError(DescribeLoginFailure(ex));
            Log.Warn($"[WS] 重新登录失败: {ex.Message}");
        }
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

        // 关键修复：server 可能是 https:// 或纯域名（Normalize 默认补了 https://），
        // 但 ClientWebSocket 必须用 wss:// 协议，因此需要转成 WebSocket 基址。
        var wsServer = ServerAddress.WsBase(server);
        var url = $"{wsServer}/ws?token={Uri.EscapeDataString(token)}" +
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
            _userInitiatedDisconnect = false;
            _wasKicked = false;
            _authRetryCount = 0;
            _reconnectAttempts = 0;
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

    /// <summary>用户主动断开：连失败提示一起清掉，"手动断开"不是错误，且不自动重连。</summary>
    public void Disconnect()
    {
        _userInitiatedDisconnect = true;
        _wasKicked = false;
        lock (_lifecycleGate)
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }
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
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
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
                    Log.Warn($"[WS] 发送失败：WebSocket 未连接 (socket={socket?.State.ToString() ?? "null"})");
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

            // 关键修复：握手成功后立即把 State 置为 Connected，而不是等第一次 ping 成功。
            // 之前 UI 和 Send 都依赖 PingLoop 里的延迟置位，导致剪贴板在握手后、心跳前发送失败。
            _dispatch(() =>
            {
                if (State != ConnectionState.Connected)
                {
                    State = ConnectionState.Connected;
                    _connectingSince = null;
                    _reconnectAttempts = 0;
                    _authRetryCount = 0;
                    AuthError = null;
                    Log.Info("[WS] 已连接服务器");
                }
            });

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
            if (!NoteAuthFailureIfNeeded(ex.Message, settings))
            {
                ScheduleReconnect(DescribeSocketFailure(ex));
            }
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
                                _reconnectAttempts = 0;
                                _authRetryCount = 0;
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
                    if (!NoteAuthFailureIfNeeded(ex.Message, settings) && State != ConnectionState.Disconnected)
                    {
                        ScheduleReconnect(DescribeSocketFailure(ex));
                    }
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

        // 服务端踢下线通知：主动断开，不重连，提示用户
        if (msg.Type == MessageType.ServerKick)
        {
            Log.Info("[WS] 👢 收到服务端踢下线通知");
            // 必须同步设置，否则紧随其后的 .failure 回调会在
            // dispatcher 之前进入 ScheduleReconnect，导致重连
            _wasKicked = true;
            lock (_lifecycleGate)
            {
                _isRunning = false;
                _reconnectTimer?.Dispose();
                _reconnectTimer = null;
            }
            _dispatch(() =>
            {
                StopInternal();
                AuthError = "密码已被管理员重置，请重新登录";
            });
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

    /// <summary>token 失效时尝试自动重新鉴权（握手错误含 401/Unauthorized）。
    /// 返回 true 表示已识别为鉴权失败并自行触发了重新登录，调用方无需再 ScheduleReconnect。</summary>
    private bool NoteAuthFailureIfNeeded(string description, SettingsStore settings)
    {
        var looksLikeAuth = (description ?? "").Contains("401")
                           || (description ?? "").Contains("Unauthorized")
                           || (description ?? "").Contains("403");
        if (!looksLikeAuth) return false;

        // 已被服务端踢下线，不再尝试重新登录
        if (_wasKicked) return true;

        // 只允许重新登录 1 次，超过说明密码已被改
        _authRetryCount += 1;
        if (_authRetryCount > 1)
        {
            Log.Warn($"[WS] 重新登录仍失败（第{_authRetryCount}次），密码可能已被重置");
            _dispatch(() =>
            {
                _wasKicked = true;
                AuthError = "密码已失效，请重新登录";
            });
            return true;
        }

        _dispatch(async () =>
        {
            Log.Info($"[WS] token 已失效，尝试用已保存的账号密码重新换取（第{_authRetryCount}次）");
            if (!settings.HasCredentials)
            {
                AuthError = "登录已失效，请到设置里填写账号密码";
                return;
            }
            lock (_lifecycleGate)
            {
                _reconnectTimer?.Dispose();
                _reconnectTimer = null;
            }
            StopInternal();
            await ReauthenticateAsync(settings).ConfigureAwait(false);
        });
        return true;
    }

    /// <summary>把登录异常翻成一句用户能照着处理的话（对齐 Mac 端 describeLoginFailure）</summary>
    public static string DescribeLoginFailure(Exception error)
    {
        if (error is AuthException aex)
        {
            switch (aex.Kind)
            {
                case AuthFailureKind.BadUrl:
                    return $"服务器地址不合法：{aex.Message}";
                case AuthFailureKind.Network:
                    return $"连不上服务器（{aex.Message}），请检查地址、网络和服务是否已启动";
                case AuthFailureKind.Server:
                    if (aex.StatusCode == 401 || aex.StatusCode == 403)
                    {
                        return $"登录失败：{aex.Message}，请检查用户名和密码";
                    }
                    return $"服务端拒绝登录：{aex.Message}";
                case AuthFailureKind.Decode:
                    return $"服务端响应异常：{aex.Message}";
            }
        }
        return $"连接失败：{error.Message}";
    }

    /// <summary>把底层 Socket/WebSocket 错误翻成用户能看懂的一句话
    /// （对齐 Mac 端 describeSocketFailure 的 NSURLError 分类）</summary>
    public static string DescribeSocketFailure(string message)
    {
        if (string.IsNullOrEmpty(message)) return "连接中断";
        return $"连接中断：{message}";
    }

    /// <summary>把异常翻成用户能看懂的一句话。优先识别 WebSocketException / SocketException /
    /// HttpRequestException 的常见错误码，对齐 Mac 端 NSURLErrorDomain 分类。</summary>
    public static string DescribeSocketFailure(Exception error)
    {
        static string SocketErrorText(SocketError se) => se switch
        {
            SocketError.HostNotFound => "找不到服务器，请检查服务器地址",
            SocketError.NoData => "DNS 解析失败，请检查服务器地址",
            SocketError.ConnectionRefused => "服务器拒绝连接，请确认地址、端口和服务是否已启动",
            SocketError.TimedOut => "连接服务器超时，请检查网络或服务器状态",
            SocketError.NetworkUnreachable => "网络不可用，请检查本机网络连接",
            SocketError.NetworkDown => "网络不可用，请检查本机网络连接",
            SocketError.NetworkReset => "网络连接已断开，请重新连接",
            SocketError.ConnectionReset => "网络连接已断开，请重新连接",
            SocketError.ConnectionAborted => "连接被中断，请重试",
            SocketError.AddressNotAvailable => "服务器地址不可用",
            SocketError.Fault => "地址族错误，请检查服务器地址格式",
            _ => "",
        };

        // 1) WebSocketException → 先看内部有没有 SocketException
        if (error is WebSocketException wsex)
        {
            if (wsex.InnerException is SocketException innerSock)
            {
                var t = SocketErrorText(innerSock.SocketErrorCode);
                if (t.Length > 0) return t;
            }
            var wsCode = wsex.WebSocketErrorCode;
            if (wsCode == WebSocketError.HeaderError || wsCode == WebSocketError.Faulted)
            {
                // 握手阶段 401 常包装成 HeaderError，给个通用提示
                if ((wsex.Message ?? "").Contains("401") || (wsex.Message ?? "").Contains("Unauthorized"))
                    return "登录已失效（Token 无效），请重新连接";
            }
            if (wsex.InnerException is not null)
            {
                var nested = DescribeSocketFailure(wsex.InnerException);
                if (!nested.StartsWith("连接中断")) return nested;
            }
        }

        // 2) SocketException → 查表
        if (error is SocketException sock)
        {
            var t = SocketErrorText(sock.SocketErrorCode);
            if (t.Length > 0) return t;
        }

        // 3) HttpRequestException（ConnectAsync 内部有时会包成这个）
        if (error is HttpRequestException httpex)
        {
            if (httpex.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || (int)(httpex.StatusCode ?? 0) == 403)
            {
                return "登录已失效，请重新连接";
            }
            if (httpex.InnerException is not null)
            {
                var nested = DescribeSocketFailure(httpex.InnerException);
                if (!nested.StartsWith("连接中断")) return nested;
            }
        }

        // 4) 关键字兜底（日志里常有「无法连接到远程服务器」之类中文/英文）
        var m = (error.Message ?? "").ToLowerInvariant();
        if (m.Contains("tls") || m.Contains("ssl") || m.Contains("certificate") || m.Contains("证书"))
            return "TLS 握手失败，请确认服务器证书配置";
        if (m.Contains("name or service not known") || m.Contains("no such host") || m.Contains("找不到主机"))
            return "找不到服务器，请检查服务器地址";
        if (m.Contains("actively refused") || m.Contains("refused") || m.Contains("拒绝连接"))
            return "服务器拒绝连接，请确认地址、端口和服务是否已启动";
        if (m.Contains("timed out") || m.Contains("超时"))
            return "连接服务器超时，请检查网络或服务器状态";
        if (m.Contains("not connected") || m.Contains("unreachable") || m.Contains("无法连接"))
            return "网络不可用，请检查本机网络连接";

        return $"连接失败：{error.Message}";
    }

    /// <summary>停止连接并调度自动重连（指数退避），除非用户主动断开或已被踢下线。
    /// 断连原因摆到界面上给用户看。</summary>
    private void ScheduleReconnect(string reason)
    {
        // 保证 "连接中" 至少显示 minConnectingDuration
        var minDelay = TimeSpan.Zero;
        if (_connectingSince.HasValue)
        {
            var elapsed = DateTime.Now - _connectingSince.Value;
            if (elapsed < MinConnectingDuration) minDelay = MinConnectingDuration - elapsed;
        }

        Task.Run(async () =>
        {
            if (minDelay > TimeSpan.Zero) await Task.Delay(minDelay).ConfigureAwait(false);

            bool skip;
            lock (_lifecycleGate) { skip = !_isRunning && !(_reconnectTimer is null); }
            // 用户主动断开 → 不重连
            if (_userInitiatedDisconnect)
            {
                Log.Info("[WS] 用户已手动断开，不自动重连");
                return;
            }
            // 被踢下线 → 不重连
            if (_wasKicked)
            {
                Log.Info("[WS] 被服务端踢下线，不自动重连");
                return;
            }

            // 清理当前连接资源，但保留 server/token 供重连使用
            ClientWebSocket? socket;
            CancellationTokenSource? cts;
            lock (_lifecycleGate)
            {
                _isRunning = false;
                socket = _socket;
                cts = _cts;
                _socket = null;
                _cts = null;
            }
            try { cts?.Cancel(); } catch { }
            try { socket?.Abort(); } catch { }
            socket?.Dispose();
            cts?.Dispose();

            // 指数退避：2, 4, 8, 16, 30, 30...
            var backoffSeconds = Math.Min(Math.Pow(2.0, _reconnectAttempts), MaxReconnectDelaySeconds);
            _reconnectAttempts += 1;
            var delay = TimeSpan.FromSeconds(Math.Max(backoffSeconds, minDelay.TotalSeconds));

            var server = _currentServer;
            var token = _currentToken;

            _dispatch(() =>
            {
                State = ConnectionState.Disconnected;
                _connectingSince = null;
                if (!string.IsNullOrEmpty(reason) && AuthError is null)
                    AuthError = reason;
            });

            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(token))
            {
                Log.Warn("[WS] 重连取消：server 或 token 已清空");
                return;
            }

            Log.Info($"[WS] {Math.Round(delay.TotalSeconds)}秒后自动重连（第{_reconnectAttempts}次）→ {server}");

            lock (_lifecycleGate)
            {
                _reconnectTimer?.Dispose();
                _reconnectTimer = new Timer(_ =>
                {
                    if (_userInitiatedDisconnect || _wasKicked) return;
                    if (string.IsNullOrEmpty(_currentServer) || string.IsNullOrEmpty(_currentToken)) return;
                    Log.Info("[WS] 正在自动重连 → " + _currentServer);
                    Start(_currentServer, _currentToken, SettingsStore.Shared);
                }, null, delay, Timeout.InfiniteTimeSpan);
            }
        });
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}
