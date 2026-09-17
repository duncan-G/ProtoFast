using System.Text;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Tree;

/// <summary>
/// Canonical serialization of a tree, and the hash over it.
///
/// <para>This is a hand-written, deterministic renderer rather than <c>JsonSerializer</c>. A
/// <c>TreeHash</c> that changed because a serializer option or a property order changed in a
/// library update would invalidate every frozen document's identity — and frozen output carries
/// an S3 object-lock retention precisely because it is supposed to be permanent (plan §9.11).
/// So the format is pinned here, in code the repository owns.</para>
/// </summary>
public static class TreeCanonicalizer
{
    public static string Canonicalize(SectionNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var builder = new StringBuilder();
        Write(builder, root);
        return builder.ToString();
    }

    public static string Hash(SectionNode root) => Ids.Sha256Hex(Canonicalize(root));

    /// <summary>
    /// A hash over paragraph identity and content together, so the frozen record pins <em>what</em>
    /// the paragraphs say as well as how they are arranged. The separator is a unit separator,
    /// which cannot occur in an id or a content hash.
    /// </summary>
    public static string HashParagraphs(IEnumerable<Paragraph> paragraphs)
    {
        const char separator = '\u001f';
        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            builder.Append(paragraph.ParagraphId).Append(separator)
                   .Append(paragraph.ContentHash).Append(separator);
        }

        return Ids.Sha256Hex(builder.ToString());
    }

    private static void Write(StringBuilder builder, SectionNode node)
    {
        builder.Append('{')
            .Append("\"id\":").Append(Quote(node.SectionId)).Append(',')
            .Append("\"title\":").Append(Quote(node.Title)).Append(',')
            .Append("\"inferred\":").Append(node.TitleInferred ? "true" : "false").Append(',')
            .Append("\"heading\":").Append(node.HeadingLineId is null ? "null" : Quote(node.HeadingLineId)).Append(',')
            .Append("\"level\":").Append(node.Level).Append(',')
            .Append("\"paragraphs\":[");

        for (var i = 0; i < node.ParagraphIds.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Quote(node.ParagraphIds[i]));
        }

        builder.Append("],\"children\":[");

        for (var i = 0; i < node.Children.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            Write(builder, node.Children[i]);
        }

        builder.Append("]}");
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
