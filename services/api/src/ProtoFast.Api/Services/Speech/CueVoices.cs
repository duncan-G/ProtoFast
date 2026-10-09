using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Speech;

public static class CueVoices
{
    /// <summary>
    /// A dialogue line speaks in its speaker's cast voice; action and narration in the cue's
    /// narrator's, or the story's first narrator's. Null leaves the provider's default.
    /// </summary>
    public static string? VoiceIdFor(CueRecord cue, SceneRecord scene, StoryRecord story)
    {
        var element = scene.Elements.FirstOrDefault(e => e.Id == cue.ElementId);
        if (element is null)
        {
            return null;
        }

        if (element.Type != SceneElementTypeRecord.Dialogue)
        {
            var narrators = story.Narrators.OrderBy(n => n.Position).ThenBy(n => n.Id).ToList();
            var narrator = narrators.FirstOrDefault(n => n.Id == cue.NarratorId) ?? narrators.FirstOrDefault();
            return narrator?.Voice.VoiceId;
        }

        return element.SpeakerId is { } speakerId
            ? story.Characters.FirstOrDefault(c => c.Id == speakerId)?.Voice?.VoiceId
            : null;
    }
}
