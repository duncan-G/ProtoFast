using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Tree;

/// <summary>
/// Assigns a level 1–6 to every detected heading, deterministically, from the evidence the
/// document already carries: numbering depth first, then font scale, then the level the
/// converter claimed.
///
/// <para>Phase 3b (plan §10.1) exists because heading levels are a <em>document-wide</em>
/// judgement that parallel windows cannot make. This is the deterministic half of it: for a
/// document whose headings are numbered or visually tiered, there is nothing left for a model to
/// decide, and the sequential LLM pass is skipped entirely. <see cref="NeedsModel"/> says when
/// that is not true.</para>
/// </summary>
public static class HeadingLevelAssigner
{
    public static IReadOnlyList<HeadingRecord> Assign(IReadOnlyList<HeadingRecord> headings)
    {
        ArgumentNullException.ThrowIfNull(headings);
        if (headings.Count == 0)
        {
            return headings;
        }

        if (headings.All(h => TextMetrics.HasNumberingPrefix(h.Text)))
        {
            return Normalize([.. headings.Select(h => h with { Level = TextMetrics.NumberingDepth(h.Text) })]);
        }

        // Distinct font scales are a visual tier system: the largest is level 1, the next
        // level 2, and so on. Bucketed to 0.05 so measurement noise does not invent tiers.
        var scales = headings
            .Select(h => h.Layout?.FontScale)
            .Where(s => s is not null)
            .Select(s => Math.Round(s!.Value / 0.05) * 0.05)
            .Distinct()
            .OrderDescending()
            .ToList();

        if (scales.Count is > 0 and <= 6 && headings.All(h => h.Layout is not null))
        {
            return Normalize([.. headings.Select(h =>
            {
                var bucket = Math.Round(h.Layout!.FontScale / 0.05) * 0.05;
                return h with { Level = scales.IndexOf(bucket) + 1 };
            })]);
        }

        // Fall back to the converter's own '#' count where it gave one.
        return Normalize([.. headings.Select(h => h with { Level = h.Level ?? 1 })]);
    }

    /// <summary>
    /// True when the deterministic evidence is too thin to trust and the sequential model pass
    /// of plan §10.1 phase 3b should run: no numbering, no layout, and the converter's levels are
    /// all the same (which tells us nothing about hierarchy).
    /// </summary>
    public static bool NeedsModel(IReadOnlyList<HeadingRecord> headings)
    {
        if (headings.Count <= 1)
        {
            return false;
        }

        if (headings.All(h => TextMetrics.HasNumberingPrefix(h.Text)))
        {
            return false;
        }

        if (headings.All(h => h.Layout is not null))
        {
            return false;
        }

        return headings.Select(h => h.Level).Distinct().Count() <= 1;
    }

    /// <summary>
    /// Clamps to 1–6 and closes gaps, so a document that jumps 1 → 4 becomes 1 → 2. The
    /// <c>tree-shape</c> check requires levels to increase by depth; an unclosed gap would fail
    /// it for a document whose hierarchy is actually fine.
    /// </summary>
    public static IReadOnlyList<HeadingRecord> Normalize(IReadOnlyList<HeadingRecord> headings)
    {
        var result = new List<HeadingRecord>(headings.Count);
        var stack = new List<int>();

        foreach (var heading in headings)
        {
            var raw = Math.Clamp(heading.Level ?? 1, 1, 6);

            while (stack.Count > 0 && stack[^1] >= raw)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            stack.Add(raw);
            result.Add(heading with { Level = Math.Min(6, stack.Count) });
        }

        return result;
    }
}
