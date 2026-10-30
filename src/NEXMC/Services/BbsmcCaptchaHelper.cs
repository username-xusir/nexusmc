namespace NEXMC.Services;

public static class BbsmcCaptchaHelper
{
    public static bool IsCaptchaFailure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("captcha_html", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("_guard/html.js", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("人机验证", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildShortMessage()
    {
        return "BBSMC CDN 人机验证拦截，已自动打开验证窗口；完成验证并下载后会自动继续安装。NEXMC 不会绕过验证。";
    }

    public static string BuildMessage()
    {
        return "BBSMC 的 CDN 返回了人机验证页，这条线路无法直接自动下载。NEXMC 不会绕过验证。"
            + Environment.NewLine
            + "请点击右侧“打开 BBSMC 验证”，在应用内完成验证并下载整合包；下载完成后会自动继续安装（仅下载模式会保存到所选文件夹）。"
            + Environment.NewLine
            + "也可以使用“选择本地整合包”导入，或切换到 Modrinth / CurseForge 来源。";
    }
}
