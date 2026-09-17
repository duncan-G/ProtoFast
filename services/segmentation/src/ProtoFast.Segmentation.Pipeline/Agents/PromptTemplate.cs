using System.Text;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// Fills <c>{{placeholder}}</c> slots in a prompt template.
///
/// <para>Deliberately not a general templating engine, and deliberately not string interpolation
/// at the call site. The order of a prompt's sections is load-bearing for prompt caching — rules,
/// then skills, then document statistics, then the variable content, so the stable prefix is
/// byte-identical across calls (plan §14.9). A template file makes that order visible and
/// reviewable; interpolation scattered through executors would not.</para>
/// </summary>
public sealed class PromptTemplate(string template)
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public PromptTemplate Set(string name, string? value)
    {
        _values[name] = value ?? string.Empty;
        return this;
    }

    public PromptTemplate Set(string name, int value) => Set(name, value.ToString());

    public string Render()
    {
        var builder = new StringBuilder(template.Length + 512);
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf("{{", index, StringComparison.Ordinal);
            if (open < 0)
            {
                builder.Append(template, index, template.Length - index);
                break;
            }

            var close = template.IndexOf("}}", open, StringComparison.Ordinal);
            if (close < 0)
            {
                builder.Append(template, index, template.Length - index);
                break;
            }

            builder.Append(template, index, open - index);
            var name = template[(open + 2)..close].Trim();

            // An unfilled placeholder renders empty rather than leaving "{{outline}}" in the
            // prompt: a document with no outline yet is an ordinary case, and literal braces in a
            // prompt read to a model as content it should do something with.
            builder.Append(_values.GetValueOrDefault(name, string.Empty));
            index = close + 2;
        }

        return CollapseBlankRuns(builder.ToString());
    }

    /// <summary>
    /// An empty placeholder leaves its heading stranded above three blank lines. Collapsing runs
    /// keeps the rendered prompt readable — which matters because these prompts are read by people
    /// during evaluation as often as by models.
    /// </summary>
    private static string CollapseBlankRuns(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var builder = new StringBuilder(text.Length);
        var blanks = 0;

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                blanks++;
                if (blanks > 1)
                {
                    continue;
                }
            }
            else
            {
                blanks = 0;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString().Trim() + "\n";
    }
}
