using Ganss.Xss;

namespace NoteKeeping.Services;

public sealed class NotebookContentSanitizer
{
    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();
    private readonly object _sync = new();

    public string Sanitize(string? html)
    {
        lock (_sync)
        {
            return _sanitizer.Sanitize(html ?? string.Empty);
        }
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(
        [
            "b", "br", "div", "em", "h2", "i", "p", "span", "strong", "u"
        ]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedCssProperties.Clear();
        return sanitizer;
    }
}
