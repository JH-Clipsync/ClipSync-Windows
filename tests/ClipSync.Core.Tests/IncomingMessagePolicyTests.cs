using ClipSync.Core.Net;
using ClipSync.Core.Protocol;
using Xunit;

namespace ClipSync.Core.Tests;

public sealed class IncomingMessagePolicyTests
{
    public static IEnumerable<object[]> LocalClipboardMessages()
    {
        yield return [ClipboardMessage("win-local", MessageKind.Text, "本机复制", null)];
        yield return [ClipboardMessage("win-local", MessageKind.Image, null, "aW1hZ2U=")];
    }

    [Theory]
    [MemberData(nameof(LocalClipboardMessages))]
    public void LocalClipboardMessage_IsBlocked(SyncMessage message)
    {
        Assert.True(IncomingMessagePolicy.IsFromLocalDevice(message, "win-local"));
    }

    [Theory]
    [InlineData(MessageKind.Text, "远端复制", null)]
    [InlineData(MessageKind.Image, null, "aW1hZ2U=")]
    public void RemoteClipboardMessage_IsAccepted(string kind, string? text, string? data)
    {
        var message = ClipboardMessage("mac-remote", kind, text, data);

        Assert.False(IncomingMessagePolicy.IsFromLocalDevice(message, "win-local"));
    }

    [Fact]
    public void MissingLocalDeviceId_DoesNotBlockMessages()
    {
        var message = ClipboardMessage("win-local", MessageKind.Text, "本机复制", null);

        Assert.False(IncomingMessagePolicy.IsFromLocalDevice(message, ""));
    }

    private static SyncMessage ClipboardMessage(string from, string kind, string? text, string? data) => new()
    {
        Id = "regression-local-source",
        Type = MessageType.Clipboard,
        From = from,
        To = "*",
        Payload = new MessagePayload
        {
            Kind = kind,
            Text = text,
            Data = data,
            Mime = kind == MessageKind.Image ? "image/png" : "text/plain",
        },
    };
}
