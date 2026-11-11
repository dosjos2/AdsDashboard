using System.Net;
using System.Text.RegularExpressions;

namespace DevOpsPulse;

public static class WorkItemText
{
    // Display server content as selectable text; never execute HTML or load remote content.
    public static string FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        static string Replace(string input, string pattern, string replacement) => Regex.Replace(input, pattern, replacement,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        try
        {
            var text = Replace(html, @"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>", "");
            text = Replace(text, @"<\s*li\b[^>]*>", "\n• ");
            text = Replace(text, @"</\s*li\s*>", "");
            text = Replace(text, @"<\s*br\b[^>]*>|</\s*(p|div|h[1-6]|tr|ul|ol)\s*>", "\n");
            text = Replace(text, @"<[^>]*>", "");
            text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
            text = Replace(text, @"[\t ]+", " ");
            text = Replace(text, @" *\r?\n *", "\n");
            return Replace(text, @"\n{3,}", "\n\n").Trim();
        }
        catch (RegexMatchTimeoutException) { return "Beskrivelsen kunne ikke vises fordi formateringen er for kompleks."; }
    }
}
