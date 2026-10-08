using System.Text;

namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <summary>A line diff between two versions of a skill's instructions or a script's source.</summary>
public static class LineDiff
{
    // Past this many cells the middle that differs is reported as wholly replaced.
    private const long MaxCells = 4_000_000;

    public static (int Added, int Removed) Count(string before, string after)
    {
        var ops = Diff(before, after);
        return (ops.Count(o => o.Kind == '+'), ops.Count(o => o.Kind == '-'));
    }

    public static string Describe(string before, string after)
    {
        var (added, removed) = Count(before, after);
        return added == 0 && removed == 0 ? "unchanged" : $"+{added}/−{removed} lines";
    }

    /// <summary>Changed lines prefixed <c>+</c> or <c>-</c>, with a few unchanged lines around each change.</summary>
    public static string Unified(string before, string after, int context = 2)
    {
        var ops = Diff(before, after);
        var keep = new bool[ops.Count];
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind == ' ')
            {
                continue;
            }

            for (var j = Math.Max(0, i - context); j <= Math.Min(ops.Count - 1, i + context); j++)
            {
                keep[j] = true;
            }
        }

        var text = new StringBuilder();
        var skipped = false;
        for (var i = 0; i < ops.Count; i++)
        {
            if (!keep[i])
            {
                skipped = true;
                continue;
            }

            if (skipped && text.Length > 0)
            {
                text.AppendLine("…");
            }

            skipped = false;
            text.Append(ops[i].Kind).Append(' ').AppendLine(ops[i].Line);
        }

        return text.ToString();
    }

    private static List<(char Kind, string Line)> Diff(string before, string after)
    {
        var a = Lines(before);
        var b = Lines(after);

        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)])
        {
            suffix++;
        }

        var ops = new List<(char, string)>(a.Length + b.Length);
        ops.AddRange(a[..prefix].Select(l => (' ', l)));
        Middle(a[prefix..^suffix], b[prefix..^suffix], ops);
        ops.AddRange(a[^suffix..].Select(l => (' ', l)));
        return ops;
    }

    private static void Middle(string[] a, string[] b, List<(char, string)> ops)
    {
        if ((long)a.Length * b.Length > MaxCells)
        {
            ops.AddRange(a.Select(l => ('-', l)));
            ops.AddRange(b.Select(l => ('+', l)));
            return;
        }

        // Longest common subsequence from the end, so the walk below reads forwards.
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                ops.Add((' ', a[x++]));
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                ops.Add(('-', a[x++]));
            }
            else
            {
                ops.Add(('+', b[y++]));
            }
        }

        ops.AddRange(a[x..].Select(l => ('-', l)));
        ops.AddRange(b[y..].Select(l => ('+', l)));
    }

    private static string[] Lines(string text) =>
        text.Length == 0 ? [] : text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}
