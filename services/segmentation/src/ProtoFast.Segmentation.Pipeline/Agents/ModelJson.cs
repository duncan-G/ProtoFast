using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// Turns a model's reply into a typed object, or into a parser error precise enough to repair
/// from (plan §10.4).
///
/// <para>A parse failure is a validation failure, not an exception to log and move past: it
/// enters the same repair loop as a semantic failure, with the parser's own message as the
/// feedback. That is why this returns a result rather than throwing.</para>
/// </summary>
public static class ModelJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        // Models emit trailing commas and the occasional // comment often enough that refusing
        // them would spend a repair round on something with one right answer.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public sealed record ParseResult<T>(T? Value, string? Error)
    {
        public bool Success => Error is null;
    }

    public static ParseResult<T> Parse<T>(string reply)
    {
        var json = Extract(reply);
        if (json.Length == 0)
        {
            return new ParseResult<T>(default, "The reply contained no JSON object.");
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(json, Options);
            return value is null
                ? new ParseResult<T>(default, "The reply parsed as JSON null.")
                : new ParseResult<T>(value, null);
        }
        catch (JsonException ex)
        {
            return new ParseResult<T>(
                default,
                $"JSON did not parse at line {ex.LineNumber}, position {ex.BytePositionInLine}: {ex.Message}");
        }
    }

    /// <summary>
    /// Pulls the JSON out of a reply that may be wrapped in a code fence or padded with prose.
    ///
    /// <para>The rules say to return JSON only, and most replies do. This handles the ones that do
    /// not, by scanning for the outermost balanced braces — with string and escape awareness, so a
    /// <c>}</c> inside a quoted title does not end the scan early.</para>
    /// </summary>
    internal static string Extract(string reply)
    {
        var text = reply.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var closing = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && closing > firstNewline)
            {
                text = text[(firstNewline + 1)..closing].Trim();
            }
        }

        var start = text.IndexOf('{');
        if (start < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;
            }
        }

        // Unbalanced: hand back what there is so the parser's own error names the truncation point,
        // which is more useful feedback than "no JSON found".
        return text[start..];
    }
}
