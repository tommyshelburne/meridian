using Markdig;

namespace Meridian.Portal.Help;

public record HelpArticle(
    string Slug,
    string Title,
    string Summary,
    IReadOnlyList<string> Tags,
    string MarkdownBody);

// Loads the curated FAQ articles from the Help/Articles folder shipped with
// the Portal. Articles are Markdown with a tiny YAML-ish frontmatter (title,
// summary, tags). Cached on first load — small enough that we don't bother
// invalidating on file change; redeploy to update.
public class HelpArticleService
{
    private readonly IWebHostEnvironment _env;
    private readonly MarkdownPipeline _pipeline;
    private IReadOnlyList<HelpArticle>? _cache;
    private readonly object _lock = new();

    public HelpArticleService(IWebHostEnvironment env)
    {
        _env = env;
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoLinks()
            .DisableHtml()  // articles are trusted, but no need to allow raw HTML
            .Build();
    }

    public IReadOnlyList<HelpArticle> All()
    {
        if (_cache is not null) return _cache;
        lock (_lock)
        {
            if (_cache is not null) return _cache;
            _cache = LoadAll();
            return _cache;
        }
    }

    public HelpArticle? GetBySlug(string slug) =>
        All().FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public string RenderHtml(string markdown) => Markdown.ToHtml(markdown, _pipeline);

    public IReadOnlyList<HelpArticle> Search(string? query)
    {
        var all = All();
        if (string.IsNullOrWhiteSpace(query)) return all;
        var q = query.Trim();
        return all
            .Where(a =>
                a.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || a.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)
                || a.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))
                || a.MarkdownBody.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private IReadOnlyList<HelpArticle> LoadAll()
    {
        var dir = Path.Combine(_env.ContentRootPath, "Help", "Articles");
        if (!Directory.Exists(dir)) return Array.Empty<HelpArticle>();

        var articles = new List<HelpArticle>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.md").OrderBy(f => f))
        {
            var raw = File.ReadAllText(file);
            var slug = Path.GetFileNameWithoutExtension(file);
            articles.Add(Parse(slug, raw));
        }
        return articles;
    }

    // Minimal frontmatter parser — `---\nkey: value\nkey: value\n---\n<body>`.
    // Sufficient for trusted in-repo content; not a general YAML implementation.
    private static HelpArticle Parse(string slug, string raw)
    {
        var title = slug;
        var summary = string.Empty;
        var tags = new List<string>();
        var body = raw;

        if (raw.StartsWith("---", StringComparison.Ordinal))
        {
            var end = raw.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end > 0)
            {
                var fm = raw[3..end];
                body = raw[(end + 4)..].TrimStart('\r', '\n');
                foreach (var line in fm.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    var colon = trimmed.IndexOf(':');
                    if (colon < 0) continue;
                    var key = trimmed[..colon].Trim();
                    var value = trimmed[(colon + 1)..].Trim();
                    switch (key.ToLowerInvariant())
                    {
                        case "title": title = value; break;
                        case "summary": summary = value; break;
                        case "tags":
                            tags = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                            break;
                    }
                }
            }
        }

        return new HelpArticle(slug, title, summary, tags, body);
    }
}
