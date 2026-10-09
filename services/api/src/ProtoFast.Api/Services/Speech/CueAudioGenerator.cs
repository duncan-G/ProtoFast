using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.Storage.Abstractions;
using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using MediaAssetKindRecord = ProtoFast.Data.ThePlot.Entities.MediaAssetKind;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;

namespace ProtoFast.Api.Services.Speech;

/// <summary>
/// Speaks a scene's lines. Each is rendered from its cue's performance (or, with no cue yet, its
/// words as written) and its speaker's or narrator's voice, the result is stored once per request
/// hash (the media cache), and the cue points at it; a line without a cue gets one. The provider
/// is called outside the story lock, so writing goes on while the lines render.
/// </summary>
public sealed class CueAudioGenerator(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    DirectionScope directionScope,
    ISpeechSynthesizer synthesizer,
    IObjectStore objectStore,
    IMediaAssetRepository assets,
    IQueryFactory<MediaAssetRecord, IMediaAssetQuery> assetQueries,
    ILogger<CueAudioGenerator> logger)
{
    /// <param name="Line">Null when an asset for the same request already exists and is reused unspoken.</param>
    private sealed record Rendered(Guid ElementId, SpeechRequest Request, string RequestHash, SpokenLine? Line);

    /// <returns>The scene with its direction, as stored after the write.</returns>
    public async Task<(SceneRecord Scene, Guid StoryId)> GenerateAsync(
        Guid sceneId, Guid? elementId, bool regenerate, CancellationToken ct)
    {
        List<Rendered> rendered = [];
        Guid storyId;
        using (unitOfWorkFactory.CreateReadOnly(nameof(GenerateAsync)))
        {
            var directed = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
            storyId = directed.Story.Id;
            var lines = directed.Scene.Elements
                .Where(e => DirectionValidator.Spoken(e.Type) && (elementId is null || e.Id == elementId))
                .OrderBy(e => e.Position)
                .ToList();
            if (elementId is not null && lines.Count == 0)
            {
                throw StoryErrors.NotFound(DirectionErrors.LineNotFound);
            }

            foreach (var element in lines)
            {
                var cue = directed.Scene.Cues.FirstOrDefault(c => c.ElementId == element.Id)
                          ?? new CueRecord { ElementId = element.Id, Performance = SceneBrief.Plain(element.Text).Trim() };
                var request = synthesizer.Prepare(cue.Performance, CueVoices.VoiceIdFor(cue, directed.Scene, directed.Story));
                var hash = SpeechRequests.Hash(synthesizer, request);
                if (!regenerate && cue.Audio?.RequestHash == hash)
                {
                    continue;
                }

                if (request.Text.Length == 0)
                {
                    continue;
                }

                // The media cache: the same request in this story was spoken before, so no call is made.
                if (!regenerate && await assets.ExistsByQueryAsync(
                        assetQueries.Create().InStory(storyId).OfKind(MediaAssetKindRecord.CueAudio).WithRequestHash(hash), ct))
                {
                    rendered.Add(new Rendered(cue.ElementId, request, hash, null));
                    continue;
                }

                var line = await synthesizer.SpeakAsync(request, ct);
                rendered.Add(new Rendered(cue.ElementId, request, hash, line));
            }
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(GenerateAsync));
        await storyScope.LockAsync(storyId, ct);
        var current = await directionScope.LoadAsync(sceneId, withEarlierScenes: false, ct);
        foreach (var render in rendered)
        {
            var cue = current.Scene.Cues.FirstOrDefault(c => c.ElementId == render.ElementId);
            if (cue is null)
            {
                var element = current.Scene.Elements.FirstOrDefault(e => e.Id == render.ElementId);
                if (element is null)
                {
                    continue;
                }

                cue = new CueRecord { ElementId = element.Id, Performance = SceneBrief.Plain(element.Text).Trim() };
                current.Scene.Cues.Add(cue);
            }

            cue.Audio = regenerate ? null : await assets.GetFirstByQueryAsync(
                assetQueries.Create().InStory(storyId).OfKind(MediaAssetKindRecord.CueAudio).WithRequestHash(render.RequestHash), ct);
            if (cue.Audio is not null)
            {
                cue.AudioAssetId = cue.Audio.Id;
                continue;
            }

            if (render.Line is null)
            {
                // The cached asset went away between the read and the write; the next pass speaks it.
                continue;
            }

            var contentHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(render.Line.Audio));
            var key = $"stories/{storyId}/audio/{contentHash}.{SpeechRequests.Extension(render.Line.ContentType)}";
            await objectStore.WriteBytesAsync(key, render.Line.Audio, render.Line.ContentType, render.RequestHash, ct);
            var asset = new MediaAssetRecord
            {
                StoryId = storyId,
                Kind = MediaAssetKindRecord.CueAudio,
                StorageKey = key,
                ContentType = render.Line.ContentType,
                ContentHash = contentHash,
                ByteSize = render.Line.Audio.Length,
                RequestHash = render.RequestHash,
                Provider = synthesizer.Provider,
                Model = synthesizer.Model,
                Voice = render.Request.Voice,
                CostUsdMicros = render.Line.CostUsdMicros,
            };
            if (regenerate)
            {
                // A fresh render of the same request replaces the cached one.
                var previous = await assets.GetFirstByQueryAsync(
                    assetQueries.Create().InStory(storyId).OfKind(MediaAssetKindRecord.CueAudio).WithRequestHash(render.RequestHash), ct);
                if (previous is not null)
                {
                    previous.StorageKey = key;
                    previous.ContentType = asset.ContentType;
                    previous.ContentHash = contentHash;
                    previous.ByteSize = asset.ByteSize;
                    previous.Voice = asset.Voice;
                    previous.CostUsdMicros += asset.CostUsdMicros;
                    cue.Audio = previous;
                    cue.AudioAssetId = previous.Id;
                    continue;
                }
            }

            await assets.AddAsync(asset, ct);
            cue.Audio = asset;
            logger.LogInformation("Spoke a line of {Scene} with {Provider}/{Model} as {Voice} ({Bytes} bytes)",
                sceneId, synthesizer.Provider, synthesizer.Model, render.Request.Voice, render.Line.Audio.Length);
        }

        await unitOfWork.CommitAsync(ct);
        return (current.Scene, storyId);
    }
}
