using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Services.Speech;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class AudioTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    private ScriptedSpeechSynthesizer Speech => database.Services.GetRequiredService<ScriptedSpeechSynthesizer>();

    private MemoryObjectStore Store => database.Services.GetRequiredService<MemoryObjectStore>();

    /// <summary>Mara (cast as v1) and the narrator; Bolt has no voice.</summary>
    private async Task<(Story Story, Scene Scene, Character Mara, Character Bolt)> SpokenSceneAsync()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var bolt = await _writer.CreateCharacterAsync(story.Id, "Bolt", "Robot");
        await _writer.Call((s, c) => s.UpdateCharacterSheet(new UpdateCharacterSheetRequest
        {
            CharacterId = mara.Id,
            Voice = new VoiceProfile { Description = "low", Baseline = "dry", VoiceId = "v1" },
        }, c));
        await _writer.Call((s, c) => s.SaveNarrators(new SaveNarratorsRequest
        {
            StoryId = story.Id,
            Narrators = { new Narrator { Name = "Ada", Voice = new VoiceProfile { Description = "warm", VoiceId = "v2" } } },
        }, c));
        var scene = await _writer.WriteFirstSceneAsync(
            story,
            Heading(),
            Dialogue(mara.Id, "Don't move."),
            Dialogue(bolt.Id, "I never do."),
            Beat(SceneElementType.Narration, "The dark held its breath."));
        var direction = new SceneDirection { SceneId = scene.Id };
        direction.Cues.Add(new Cue { ElementId = scene.Elements[1].Id, Performance = "[whispers] Don't move." });
        direction.Cues.Add(new Cue { ElementId = scene.Elements[2].Id, Performance = "I never do." });
        direction.Cues.Add(new Cue { ElementId = scene.Elements[3].Id, Performance = "The dark held its breath." });
        await _writer.SaveDirectionAsync(direction);
        return (story, scene, mara, bolt);
    }

    [Fact]
    public async Task Generating_speaks_every_cue_in_its_voice_stores_the_audio_once_and_points_the_cues_at_it()
    {
        var (story, scene, _, _) = await SpokenSceneAsync();
        var before = Speech.Spoken.Count;

        var reply = await _writer.Direct((d, c) => d.GenerateAudio(new GenerateAudioRequest { SceneId = scene.Id }, c));

        var cues = reply.Direction.Cues;
        Assert.All(cues, cue => Assert.True(cue.Audio is { Stale: false }));
        Assert.Equal(["v1", "default", "v2"], cues.Select(c => c.Audio.Voice));
        Assert.Equal(3, Speech.Spoken.Count - before);
        Assert.Contains(Speech.Spoken, r => r.Text == "Don't move." && r.Instructions == "whispers" && r.Voice == "v1");
        Assert.All(cues, cue => Assert.StartsWith($"https://media.test/stories/{story.Id}/audio/", cue.Audio.Url));
        Assert.All(cues, cue => Assert.EndsWith(".mp3", cue.Audio.Url));
        Assert.True(Store.Objects.ContainsKey(cues[0].Audio.Url.Replace("https://media.test/", "")));

        // Nothing changed: a second pass speaks nothing.
        await _writer.Direct((d, c) => d.GenerateAudio(new GenerateAudioRequest { SceneId = scene.Id }, c));
        Assert.Equal(3, Speech.Spoken.Count - before);

        // The audio comes back with the direction, and reads stale once the performance changes.
        var direction = await _writer.GetDirectionAsync(scene.Id);
        Assert.All(direction.Cues, cue => Assert.False(cue.Audio.Stale));
        direction.Cues[0].Performance = "[angry] Don't move.";
        var saved = await _writer.SaveDirectionAsync(direction);
        Assert.True(saved.Cues[0].Audio.Stale);
        Assert.False(saved.Cues[1].Audio.Stale);

        // Generating one line re-speaks that line only, and the old asset is reused when the request returns to it.
        var one = await _writer.Direct((d, c) => d.GenerateAudio(
            new GenerateAudioRequest { SceneId = scene.Id, ElementId = scene.Elements[1].Id }, c));
        Assert.False(one.Direction.Cues[0].Audio.Stale);
        Assert.Equal(4, Speech.Spoken.Count - before);
        var back = await _writer.GetDirectionAsync(scene.Id);
        back.Cues[0].Performance = "[whispers] Don't move.";
        var restored = await _writer.SaveDirectionAsync(back);
        var reused = await _writer.Direct((d, c) => d.GenerateAudio(new GenerateAudioRequest { SceneId = scene.Id }, c));
        Assert.Equal(4, Speech.Spoken.Count - before);
        Assert.Equal(cues[0].Audio.AssetId, reused.Direction.Cues[0].Audio.AssetId);
        Assert.True(restored.Cues[0].Audio.Stale);
    }

    [Fact]
    public async Task Regenerating_speaks_a_line_again_and_replaces_its_audio()
    {
        var (_, scene, _, _) = await SpokenSceneAsync();
        var first = await _writer.Direct((d, c) => d.GenerateAudio(new GenerateAudioRequest { SceneId = scene.Id }, c));
        var before = Speech.Spoken.Count;

        var again = await _writer.Direct((d, c) => d.GenerateAudio(
            new GenerateAudioRequest { SceneId = scene.Id, ElementId = scene.Elements[2].Id, Regenerate = true }, c));

        Assert.Equal(1, Speech.Spoken.Count - before);
        Assert.Equal(first.Direction.Cues[1].Audio.AssetId, again.Direction.Cues[1].Audio.AssetId);
        Assert.False(again.Direction.Cues[1].Audio.Stale);
    }

    [Fact]
    public async Task Changing_a_speakers_voice_makes_their_lines_stale()
    {
        var (_, scene, mara, _) = await SpokenSceneAsync();
        await _writer.Direct((d, c) => d.GenerateAudio(new GenerateAudioRequest { SceneId = scene.Id }, c));

        await _writer.Call((s, c) => s.UpdateCharacterSheet(new UpdateCharacterSheetRequest
        {
            CharacterId = mara.Id,
            Voice = new VoiceProfile { VoiceId = "v2" },
        }, c));

        var direction = await _writer.GetDirectionAsync(scene.Id);
        Assert.True(direction.Cues[0].Audio.Stale);
        Assert.False(direction.Cues[1].Audio.Stale);
    }

    [Fact]
    public async Task Only_spoken_lines_can_be_spoken_and_voices_are_listed()
    {
        var (_, scene, _, _) = await SpokenSceneAsync();

        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.Direct((d, c) => d.GenerateAudio(
            new GenerateAudioRequest { SceneId = scene.Id, ElementId = scene.Elements[0].Id }, c)));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);

        var voices = await _writer.Direct((d, c) => d.ListVoices(new ListVoicesRequest(), c));
        Assert.Equal("scripted", voices.Provider);
        Assert.Equal(["Vivian", "Ryan"], voices.Voices.Select(v => v.Name));

        var narrators = await _writer.Direct((d, c) => d.SearchVoices(
            new SearchVoicesRequest { Search = "warm", Gender = "female", Age = "middle_aged", Accent = "british", Narration = true }, c));
        var ada = Assert.Single(narrators.Voices);
        Assert.Equal(("n1", "female · middle aged · british", "https://example.test/ada.mp3"), (ada.Id, ada.Traits, ada.PreviewUrl));
        Assert.Equal(new VoiceQuery("en", "warm", "female", "middle_aged", "british", Narration: true), Speech.Searches[^1]);
    }

    [Fact]
    public async Task An_action_line_without_a_cue_is_read_by_its_narrator_and_gets_a_cue()
    {
        var (story, scene, _, _) = await SpokenSceneAsync();
        var ada = (await _writer.GetStoryAsync(story.Id)).Narrators.Single();
        var narrators = await _writer.Call((s, c) => s.SaveNarrators(new SaveNarratorsRequest
        {
            StoryId = story.Id,
            Narrators =
            {
                ada,
                new Narrator { Name = "Radio operator", Voice = new VoiceProfile { Description = "clipped", VoiceId = "v3" } },
            },
        }, c));
        var operatorId = narrators.Narrators[1].Id;
        scene.Elements.Add(Beat(SceneElementType.Action, "Static. Then a thin, rising tone."));
        await _writer.SaveSceneAsync(scene);
        scene = await _writer.GetSceneAsync(scene.Id);
        var action = scene.Elements[4];

        var spoken = await _writer.Direct((d, c) => d.GenerateAudio(
            new GenerateAudioRequest { SceneId = scene.Id, ElementId = action.Id }, c));
        var cue = spoken.Direction.Cues.Single(c => c.ElementId == action.Id);
        Assert.Equal(("Static. Then a thin, rising tone.", "v2", false), (cue.Performance, cue.Audio.Voice, cue.Edited));

        // Another narrator picked on the line makes its audio stale; deleting them hands it back.
        var direction = await _writer.GetDirectionAsync(scene.Id);
        direction.Cues.Single(c => c.ElementId == action.Id).NarratorId = operatorId;
        var picked = await _writer.SaveDirectionAsync(direction);
        Assert.True(picked.Cues.Single(c => c.ElementId == action.Id).Audio.Stale);
        var again = await _writer.Direct((d, c) => d.GenerateAudio(new GenerateAudioRequest { SceneId = scene.Id, ElementId = action.Id }, c));
        Assert.Equal("v3", again.Direction.Cues.Single(c => c.ElementId == action.Id).Audio.Voice);

        await _writer.Call((s, c) => s.SaveNarrators(new SaveNarratorsRequest { StoryId = story.Id, Narrators = { ada } }, c));
        var after = (await _writer.GetDirectionAsync(scene.Id)).Cues.Single(c => c.ElementId == action.Id);
        Assert.False(after.HasNarratorId);
        Assert.True(after.Audio.Stale);
    }

    [Fact]
    public async Task Narrators_round_trip_on_the_story_in_order()
    {
        var (story, _, _, _) = await SpokenSceneAsync();
        var loaded = await _writer.GetStoryAsync(story.Id);
        Assert.Equal(("Ada", "v2"), (loaded.Narrators.Single().Name, loaded.Narrators[0].Voice.VoiceId));
        Assert.Equal("v1", loaded.Characters.Single(c => c.Name == "Mara").Voice.VoiceId);

        var reordered = await _writer.Call((s, c) => s.SaveNarrators(new SaveNarratorsRequest
        {
            StoryId = story.Id,
            Narrators = { new Narrator { Name = "Bea", Voice = new VoiceProfile() }, loaded.Narrators[0] },
        }, c));
        Assert.Equal(["Bea", "Ada"], reordered.Narrators.Select(n => n.Name));
        Assert.Equal(loaded.Narrators[0].Id, reordered.Narrators[1].Id);
        Assert.Equal(reordered.Narrators, (await _writer.GetStoryAsync(story.Id)).Narrators);

        await _writer.Call((s, c) => s.SaveNarrators(new SaveNarratorsRequest { StoryId = story.Id }, c));
        Assert.Empty((await _writer.GetStoryAsync(story.Id)).Narrators);
    }
}
