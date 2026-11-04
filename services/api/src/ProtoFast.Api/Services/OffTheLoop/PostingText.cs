using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ProtoFast.Data.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// A posting's text off its page: the JobPosting it declares for search engines, as JSON-LD or
/// microdata, or else its main content. Headings and paragraphs keep a blank line between them.
/// </summary>
public static partial class PostingText
{
    // Less is a page that loads its posting with script, or no posting.
    private const int Shortest = 200;

    private const string Bullet = "• ";

    private static readonly HashSet<string> Skipped =
    [
        "script", "style", "noscript", "template", "svg", "canvas", "iframe", "object", "nav", "footer", "aside",
        "form", "button", "select", "dialog",
    ];

    private static readonly HashSet<string> Paragraphs =
    [
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "dl", "table", "blockquote", "pre", "section",
        "article", "header", "figure", "hr",
    ];

    private static readonly HashSet<string> Lines = ["div", "li", "tr", "dt", "dd"];

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Empty when the page shows no posting.</summary>
    public static string Read(string html)
    {
        var page = new HtmlParser().ParseDocument(html);
        var text = Candidates(page).FirstOrDefault(candidate => candidate.Length >= Shortest) ?? "";
        return text.Length <= OffTheLoopLimits.Posting ? text : text[..OffTheLoopLimits.Posting].TrimEnd();
    }

    private static IEnumerable<string> Candidates(IDocument page)
    {
        yield return Declared(page);
        yield return Marked(page);
        yield return page.QuerySelector("main, [role='main'], article") is { } main ? Text(main) : "";
    }

    private static string Declared(IDocument page)
    {
        foreach (var script in page.QuerySelectorAll("script[type='application/ld+json']"))
        {
            using var json = Parse(script.TextContent);
            if (json is not null && JobPosting(json.RootElement) is { } posting)
            {
                return Titled(Value(posting, "title"), Html(Value(posting, "description")));
            }
        }

        return "";
    }

    private static string Marked(IDocument page)
    {
        var posting = page.QuerySelectorAll("[itemscope][itemtype]").FirstOrDefault(item =>
            item.GetAttribute("itemtype")!.TrimEnd('/').EndsWith("schema.org/JobPosting", StringComparison.OrdinalIgnoreCase));
        return posting is not null && Property(posting, "description") is { } description
            ? Titled(
                Property(posting, "title")?.TextContent ?? "",
                description.LocalName == "meta" ? Html(description.GetAttribute("content") ?? "") : Text(description))
            : "";
    }

    /// <summary>A property belongs to the nearest item around it, so a nested employer's description isn't the posting's.</summary>
    private static IElement? Property(IElement item, string name) =>
        item.QuerySelectorAll($"[itemprop='{name}']")
            .FirstOrDefault(property => property.ParentElement?.Closest("[itemscope]") == item);

    private static JsonDocument? Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json, Lenient);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A JSON-LD block may list several things, or nest them in a graph.</summary>
    private static JsonElement? JobPosting(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (JobPosting(item) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case JsonValueKind.Object when element.TryGetProperty("@type", out var type) && IsJobPosting(type):
                return element;
            case JsonValueKind.Object when element.TryGetProperty("@graph", out var graph):
                return JobPosting(graph);
            default:
                return null;
        }
    }

    private static bool IsJobPosting(JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => type.GetString()!.EndsWith("JobPosting", StringComparison.Ordinal),
        JsonValueKind.Array => type.EnumerateArray().Any(IsJobPosting),
        _ => false,
    };

    private static string Value(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static string Titled(string title, string text)
    {
        title = Whitespace().Replace(title, " ").Trim();
        return title.Length == 0 || text.StartsWith(title, StringComparison.OrdinalIgnoreCase) ? text : $"{title}\n\n{text}";
    }

    /// <summary>A description is HTML, sometimes escaped twice, or plain text with its own lines.</summary>
    private static string Html(string html)
    {
        if (!html.Contains('<'))
        {
            html = WebUtility.HtmlDecode(html);
            if (!html.Contains('<'))
            {
                return Tidy(html);
            }
        }

        return Text(new HtmlParser().ParseDocument(html).Body!);
    }

    private static string Text(IElement root)
    {
        var text = new StringBuilder();
        var breaks = 0;
        var bullet = false;
        Write(root, text, ref breaks, ref bullet);
        return Tidy(text.ToString());
    }

    /// <summary>Line breaks wait for the next text, so nested blocks don't stack them up.</summary>
    private static void Write(INode node, StringBuilder text, ref int breaks, ref bool bullet)
    {
        if (node is IText piece)
        {
            Append(text, piece.Data, ref breaks, ref bullet);
            return;
        }

        if (node is not IElement element
            || Skipped.Contains(element.LocalName)
            || element.HasAttribute("hidden")
            || element.GetAttribute("aria-hidden") == "true")
        {
            return;
        }

        if (element.LocalName == "br")
        {
            breaks = Math.Min(breaks + 1, 2);
            return;
        }

        var level = Paragraphs.Contains(element.LocalName) ? 2 : Lines.Contains(element.LocalName) ? 1 : 0;
        breaks = Math.Max(breaks, level);
        bullet |= element.LocalName == "li";
        foreach (var child in element.ChildNodes)
        {
            Write(child, text, ref breaks, ref bullet);
        }

        breaks = Math.Max(breaks, level);
    }

    private static void Append(StringBuilder text, string piece, ref int breaks, ref bool bullet)
    {
        piece = Whitespace().Replace(piece, " ");
        if (piece.Trim().Length == 0)
        {
            if (breaks == 0 && !bullet)
            {
                text.Append(' ');
            }

            return;
        }

        if (breaks > 0 && text.Length > 0)
        {
            text.Append('\n', breaks);
        }

        if (bullet)
        {
            text.Append(Bullet);
        }

        breaks = 0;
        bullet = false;
        text.Append(piece);
    }

    private static string Tidy(string text)
    {
        var lines = text.Split('\n').Select(line => Whitespace().Replace(line, " ").Trim());
        var tidy = BlankLines().Replace(string.Join('\n', lines), "\n\n");
        return ListedItems().Replace(tidy, "$1\n").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    // Items wrapped in paragraphs still list one to a line.
    [GeneratedRegex(@"(?m)^(• .*)\n\n(?=• )")]
    private static partial Regex ListedItems();
}
