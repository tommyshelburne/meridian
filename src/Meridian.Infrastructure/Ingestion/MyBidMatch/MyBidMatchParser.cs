using HtmlAgilityPack;

namespace Meridian.Infrastructure.Ingestion.MyBidMatch;

public static class MyBidMatchParser
{
    private const string ApexPhoneMarker = "801-538-8775";
    private const string ThirdPartyDisclaimerSuffix = "potential business opportunity.";

    public static IReadOnlyList<string> ParseDocGroupIds(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        return doc.DocumentNode
            .SelectNodes("//a[@href]")
            ?.Select(a => a.GetAttributeValue("href", ""))
            .Where(href => href.Contains("?doc=", StringComparison.OrdinalIgnoreCase))
            .Select(href =>
            {
                var idx = href.IndexOf("?doc=", StringComparison.OrdinalIgnoreCase);
                return href[(idx + 5)..].Split('&')[0];
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList() ?? [];
    }

    public static IReadOnlyList<string> ParseArticleIds(string html, string docId)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        return doc.DocumentNode
            .SelectNodes("//a[@href]")
            ?.Select(a => a.GetAttributeValue("href", ""))
            .Where(href => href.Contains("/article", StringComparison.OrdinalIgnoreCase) &&
                           href.Contains("seq=", StringComparison.OrdinalIgnoreCase))
            .Select(href =>
            {
                var idx = href.IndexOf("seq=", StringComparison.OrdinalIgnoreCase);
                var seq = href[(idx + 4)..].Split('&')[0];
                return $"{docId}:{seq}";
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList() ?? [];
    }

    public static ParsedArticle ParseArticle(string html, string externalId)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var h4 = doc.DocumentNode.SelectSingleNode("//h4");
        var agency = h4 is not null
            ? HtmlEntity.DeEntitize(h4.InnerText).Trim()
            : string.Empty;

        var body = string.Empty;
        if (h4 is not null)
        {
            var sb = new System.Text.StringBuilder();
            for (var node = h4.NextSibling; node is not null; node = node.NextSibling)
                sb.Append(HtmlEntity.DeEntitize(node.InnerText));
            body = StripThirdPartyDisclaimer(sb.ToString()).Trim();
        }
        else
        {
            body = StripThirdPartyDisclaimer(StripPreamble(
                HtmlEntity.DeEntitize(doc.DocumentNode.InnerText))).Trim();
        }

        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var title = lines.FirstOrDefault() ?? string.Empty;

        var dashIdx = title.IndexOf("--", StringComparison.Ordinal);
        if (dashIdx < 0) dashIdx = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx >= 0 && dashIdx < 5)
            title = title[(dashIdx + (title[dashIdx] == '-' ? 2 : 3))..].TrimStart();

        foreach (var marker in new[] { " SOL ", " DUE ", " Due " })
        {
            var idx = title.IndexOf(marker, StringComparison.Ordinal);
            if (idx > 0) title = title[..idx].Trim();
        }

        return new ParsedArticle(externalId, title, agency, body);
    }

    private static string StripPreamble(string text)
    {
        var markerIdx = text.IndexOf(ApexPhoneMarker, StringComparison.Ordinal);
        if (markerIdx < 0) return text;

        var afterMarker = markerIdx + ApexPhoneMarker.Length;
        return afterMarker < text.Length ? text[afterMarker..] : string.Empty;
    }

    private static string StripThirdPartyDisclaimer(string text)
    {
        var lines = text.Split('\n').ToList();
        var disclaimerIdx = lines.FindIndex(l =>
            l.TrimEnd().EndsWith(ThirdPartyDisclaimerSuffix, StringComparison.OrdinalIgnoreCase));

        if (disclaimerIdx < 0) return text;

        var start = disclaimerIdx;
        while (start > 0 && !string.IsNullOrWhiteSpace(lines[start - 1]))
            start--;

        lines.RemoveRange(start, disclaimerIdx - start + 1);
        return string.Join('\n', lines);
    }
}

public sealed record ParsedArticle(
    string ExternalId,
    string Title,
    string Agency,
    string Body);
