namespace ClipSync.Core.Net;

/// <summary>连接状态。与 Mac 端 WSClient.ConnectionState 一致。</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}
