namespace ClipSync.Core.Net;

using ClipSync.Core.Protocol;

/// <summary>入站消息是否允许进入本机的历史、剪贴板和通知链路。</summary>
public static class IncomingMessagePolicy
{
    /// <summary>
    /// 本机设备发出的回环消息不应再次通知本机。
    /// 本机 ID 缺失时不过滤，避免误拦所有远端消息。
    /// </summary>
    public static bool IsFromLocalDevice(SyncMessage message, string localDeviceId) =>
        !string.IsNullOrWhiteSpace(localDeviceId)
        && string.Equals(message.From, localDeviceId, StringComparison.Ordinal);
}
