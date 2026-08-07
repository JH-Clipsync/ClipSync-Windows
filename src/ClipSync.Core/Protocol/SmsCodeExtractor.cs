using System.Text.RegularExpressions;

namespace ClipSync.Core.Protocol;

// ============================================================
// SmsCodeExtractor：从短信文本中提取验证码
// 覆盖（与 Mac 端 SmsCodeExtractor.swift 规则一致）：
//   - Google 专属 G-123456
//   - 中文关键词：验证码 / 校验码 / 动态密码 / 验证代码
//   - 英文关键词：code / verification / OTP / PIN / passcode
//   - 反向表述："123456 是您的验证码"
//   - 兜底：整段短信中只出现一次的 4-8 位纯数字
// ============================================================
public static partial class SmsCodeExtractor
{
    public static string? Extract(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;

        // 1) Google 专属：G-123456
        var m = GooglePattern().Match(body);
        if (m.Success) return m.Groups[1].Value;

        // 2) 关键词 → 附近数字
        m = KeywordPattern().Match(body);
        if (m.Success) return m.Groups[1].Value;

        // 3) 反向：数字在前 → "是您的验证码"
        m = ReversePattern().Match(body);
        if (m.Success) return m.Groups[1].Value;

        // 4) 兜底：整段只有唯一的 4-8 位数字
        var all = StandalonePattern().Matches(body);
        if (all.Count == 1) return all[0].Groups[1].Value;

        return null;
    }

    [GeneratedRegex(@"G-(\d{4,8})", RegexOptions.IgnoreCase)]
    private static partial Regex GooglePattern();

    [GeneratedRegex(
        @"(?:验证码|校验码|动态密码|验证代码|verification\s*code|verify\s*code|security\s*code|one[-\s]?time\s*password|otp|pin\s*code|passcode|code)[^0-9]{0,12}(\d{4,8})",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeywordPattern();

    [GeneratedRegex(
        @"(\d{4,8})[^0-9]{0,6}(?:是|为)?[^0-9]{0,4}(?:验证码|校验码|动态密码|is your\s*(?:verification\s*)?code)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ReversePattern();

    [GeneratedRegex(@"\b(\d{4,8})\b")]
    private static partial Regex StandalonePattern();
}
