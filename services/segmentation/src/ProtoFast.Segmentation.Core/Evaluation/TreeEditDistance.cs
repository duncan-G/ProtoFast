using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Evaluation;

/// <summary>
/// Zhang–Shasha tree edit distance over section trees, normalized by the size of the larger tree
/// (Appendix D).
///
/// <para>Nodes are compared by their <em>paragraph span</em>, not by title: two trees that put
/// the same paragraphs in the same shape are the same structure even when the inferred titles
/// differ, and title quality is measured separately. That is what makes this metric about
/// hierarchy rather than about wording.</para>
/// </summary>
public static class TreeEditDistance
{
    public static double Normalized(SectionNode reference, SectionNode hypothesis)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(hypothesis);

        var left = Flatten(reference);
        var right = Flatten(hypothesis);
        var larger = Math.Max(left.Count, right.Count);
        return larger == 0 ? 0 : (double)Distance(left, right) / larger;
    }

    public static int Distance(SectionNode reference, SectionNode hypothesis) =>
        Distance(Flatten(reference), Flatten(hypothesis));

    /// <summary>
    /// The post-order traversal Zhang–Shasha operates on: each node's label plus the post-order
    /// index of its leftmost leaf descendant.
    /// </summary>
    private sealed record Node(string Label, int LeftmostLeaf);

    private static List<Node> Flatten(SectionNode root)
    {
        var nodes = new List<Node>();
        Visit(root, nodes);
        return nodes;

        static int Visit(SectionNode node, List<Node> nodes)
        {
            int? leftmost = null;
            foreach (var child in node.Children)
            {
                var childLeftmost = Visit(child, nodes);
                leftmost ??= childLeftmost;
            }

            var index = nodes.Count;
            nodes.Add(new Node(Label(node), leftmost ?? index));
            return leftmost ?? index;
        }

        static string Label(SectionNode node) =>
            node.ParagraphIds.Count > 0
                ? $"leaf[{node.ParagraphIds[0]}..{node.ParagraphIds[^1]}]"
                : $"section[{node.Children.Count}]";
    }

    /// <summary>
    /// Zhang–Shasha proper, in its 1-based formulation. The paper's indices are 1-based and the
    /// forest-distance recurrence subtracts offsets from them; translating that to 0-based arrays
    /// in place is exactly where this algorithm is usually got wrong, so the translation happens
    /// once, at the two <c>Label</c>/<c>Leftmost</c> accessors, and the body stays 1-based.
    /// </summary>
    private static int Distance(List<Node> left, List<Node> right)
    {
        if (left.Count == 0)
        {
            return right.Count;
        }

        if (right.Count == 0)
        {
            return left.Count;
        }

        var treeDistance = new int[left.Count + 1, right.Count + 1];

        foreach (var i in Keyroots(left))
        {
            foreach (var j in Keyroots(right))
            {
                ForestDistance(left, right, i, j, treeDistance);
            }
        }

        return treeDistance[left.Count, right.Count];
    }

    private static void ForestDistance(
        List<Node> left, List<Node> right, int i, int j, int[,] treeDistance)
    {
        var rowOffset = Leftmost(left, i) - 1;
        var columnOffset = Leftmost(right, j) - 1;
        var rows = i - rowOffset;
        var columns = j - columnOffset;

        var forest = new int[rows + 1, columns + 1];

        for (var x = 1; x <= rows; x++)
        {
            forest[x, 0] = forest[x - 1, 0] + 1; // delete
        }

        for (var y = 1; y <= columns; y++)
        {
            forest[0, y] = forest[0, y - 1] + 1; // insert
        }

        for (var x = 1; x <= rows; x++)
        {
            for (var y = 1; y <= columns; y++)
            {
                var leftNode = x + rowOffset;
                var rightNode = y + columnOffset;

                if (Leftmost(left, leftNode) == Leftmost(left, i)
                    && Leftmost(right, rightNode) == Leftmost(right, j))
                {
                    // Both are whole subtrees rooted at this keyroot: rename is available, and the
                    // result is a tree-to-tree distance worth memoizing.
                    var rename = Label(left, leftNode) == Label(right, rightNode) ? 0 : 1;
                    forest[x, y] = Min3(
                        forest[x - 1, y] + 1,
                        forest[x, y - 1] + 1,
                        forest[x - 1, y - 1] + rename);
                    treeDistance[leftNode, rightNode] = forest[x, y];
                }
                else
                {
                    var previousRow = Leftmost(left, leftNode) - 1 - rowOffset;
                    var previousColumn = Leftmost(right, rightNode) - 1 - columnOffset;
                    forest[x, y] = Min3(
                        forest[x - 1, y] + 1,
                        forest[x, y - 1] + 1,
                        forest[previousRow, previousColumn] + treeDistance[leftNode, rightNode]);
                }
            }
        }
    }

    /// <summary>1-based post-order index of node <paramref name="index"/>'s leftmost leaf descendant.</summary>
    private static int Leftmost(List<Node> nodes, int index) => nodes[index - 1].LeftmostLeaf + 1;

    private static string Label(List<Node> nodes, int index) => nodes[index - 1].Label;

    /// <summary>
    /// Zhang–Shasha's keyroots: the root, plus every node that has a left sibling. Equivalently,
    /// the nodes that are the <em>last</em> in post-order to share their leftmost leaf.
    /// </summary>
    private static List<int> Keyroots(List<Node> nodes)
    {
        var seen = new HashSet<int>();
        var keyroots = new List<int>();
        for (var i = nodes.Count; i >= 1; i--)
        {
            if (seen.Add(Leftmost(nodes, i)))
            {
                keyroots.Add(i);
            }
        }

        keyroots.Sort();
        return keyroots;
    }

    private static int Min3(int a, int b, int c) => Math.Min(a, Math.Min(b, c));
}
