using ProtoFast.Storage.Abstractions;
using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Speech;

/// <summary>A cue's audio as the client plays it: a presigned URL, and whether the line has moved on since it was spoken.</summary>
public sealed class CueAudioMessages(ISpeechSynthesizer synthesizer, IPresignedUrlFactory urls)
{
    public Func<CueRecord, CueAudio?> For(SceneRecord scene, StoryRecord story) => cue =>
    {
        if (cue.Audio is not { } audio)
        {
            return null;
        }

        var stale = true;
        try
        {
            var request = synthesizer.Prepare(cue.Performance, CueVoices.VoiceIdFor(cue, scene, story));
            stale = SpeechRequests.Hash(synthesizer, request) != audio.RequestHash;
        }
        catch (SpeechException)
        {
            // No voice can be resolved any more: whatever was spoken no longer matches.
        }

        return new CueAudio
        {
            AssetId = audio.Id.ToString(),
            Url = urls.PresignGet(audio.StorageKey),
            ContentType = audio.ContentType,
            Stale = stale,
            Voice = audio.Voice ?? "",
        };
    };
}
