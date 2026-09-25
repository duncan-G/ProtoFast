using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>Ports the client's <c>renameReferences</c>.</summary>
public static class MentionRewriter
{
    public static void Rename(SceneElementRecord element, Func<SceneElementMentionRecord, bool> isTarget, string name)
    {
        if (element.Text is null)
        {
            return;
        }

        var reference = "@" + name;
        var text = element.Text;
        var shift = 0;
        foreach (var mention in element.Mentions.OrderBy(m => m.Offset).ToList())
        {
            var offset = mention.Offset + shift;
            mention.Offset = offset;
            if (!isTarget(mention))
            {
                continue;
            }

            text = string.Concat(text.AsSpan(0, offset), reference, text.AsSpan(offset + mention.Length));
            shift += reference.Length - mention.Length;
            mention.Length = reference.Length;
        }

        element.Text = text;
    }
}
