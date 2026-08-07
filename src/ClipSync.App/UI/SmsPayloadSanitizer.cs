using System.Text.RegularExpressions;

namespace ClipSync.App.UI;

using ClipSync.Core.Protocol;

// ============================================================
// SmsPayloadSanitizer：短信展示层面的清洗
// 与 Mac 端 SmsPayloadSanitizer 保持一致：
//   1) 去掉前导的【xxx】服务商标记块（保留【号码】作为 sender 提取）
//   2) 去掉 [N条] / [xN] 合并提示
//   3) 去掉开头省略号
//   4) 从文本里提取发件人手机号兜底
// ============================================================
public static partial class SmsPayloadSanitizer
{
    [GeneratedRegex(@"^【([^】]+?)】")]
    private static partial Regex LeadingBracketTag();

    [GeneratedRegex(@"【(\+?\d[\d\-\s]{5,}\d)】")]
    private static partial Regex PhoneBracket();

    [GeneratedRegex(@"\[(?:\d+\s*条|x\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex MergeHint();

    [GeneratedRegex(@"^[\.\s…·]+")]
    private static partial Regex LeadingEllipsis();

    /// <summary>文本里是否带短信特征标记（【xxx】 或 [N条]）。</summary>
    public static bool HasSmsMarkers(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return LeadingBracketTag().IsMatch(text)
               || MergeHint().IsMatch(text)
               || PhoneBracket().IsMatch(text);
    }

    public readonly record struct SmsResult(string Text, string? Sender);

    public static SmsResult Sanitize(string text, string? sender)
    {
        var raw = text ?? "";

        // 1) 从【号码】兜底抽手机号（优先 sender 参数，没给再自己抽）
        string? resolvedSender = sender;
        if (string.IsNullOrEmpty(resolvedSender))
        {
            var m = PhoneBracket().Match(raw);
            if (m.Success) resolvedSender = CleanPhone(m.Groups[1].Value);
        }
        else
        {
            resolvedSender = CleanPhone(resolvedSender);
        }

        // 2) 去掉前导【xxx】服务商块（注意别把【号码】也删了，sender 已抽出就没事）
        var cleaned = LeadingBracketTag().Replace(raw, "");

        // 3) 去掉 [N条] / [x2] 这类合并提示
        cleaned = MergeHint().Replace(cleaned, "");

        // 4) 去掉开头省略号 / 空白
        cleaned = LeadingEllipsis().Replace(cleaned, "");
        cleaned = cleaned.Trim();

        return new SmsResult(cleaned, resolvedSender);
    }

    /// <summary>去掉 +86 前缀、空格、短横线，让号码展示更统一。</summary>
    private static string CleanPhone(string phone)
    {
        var s = Regex.Replace(phone ?? "", @"[\s\-]", "");
        if (s.StartsWith("+86")) s = s[3..];
        else if (s.StartsWith("86") && s.Length > 10) s = s[2..];
        return s;
    }
}
