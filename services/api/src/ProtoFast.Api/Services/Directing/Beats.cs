using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;

using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Code cuts, the model labels: the scene grouped into candidate beats. An action or description
/// line is a beat of its own; a run of dialogue and narration between them is one beat; headings
/// join the beat after them and transitions the beat before.
/// </summary>
public static class Beats
{
    public sealed record Beat(int First, int Last, bool IsDialogue);

    public static IReadOnlyList<Beat> Group(IReadOnlyList<SceneElementRecord> elements)
    {
        var beats = new List<Beat>();
        int? openFirst = null;
        var openDialogue = false;
        for (var i = 0; i < elements.Count; i++)
        {
            var type = elements[i].Type;
            switch (type)
            {
                case SceneElementTypeRecord.Heading:
                    Close(i - 1);
                    openFirst = i;
                    openDialogue = false;
                    break;
                case SceneElementTypeRecord.Transition:
                    if (openFirst is null && beats.Count > 0)
                    {
                        beats[^1] = beats[^1] with { Last = i };
                    }
                    else
                    {
                        openFirst ??= i;
                        Close(i);
                    }

                    break;
                case SceneElementTypeRecord.Action:
                case SceneElementTypeRecord.Description:
                    if (openDialogue)
                    {
                        Close(i - 1);
                    }

                    openFirst ??= i;
                    openDialogue = false;
                    Close(i);
                    break;
                default:
                    openFirst ??= i;
                    openDialogue = true;
                    break;
            }
        }

        Close(elements.Count - 1);
        return beats;

        void Close(int last)
        {
            if (openFirst is { } first && last >= first)
            {
                beats.Add(new Beat(first, last, openDialogue));
            }

            openFirst = null;
            openDialogue = false;
        }
    }
}
