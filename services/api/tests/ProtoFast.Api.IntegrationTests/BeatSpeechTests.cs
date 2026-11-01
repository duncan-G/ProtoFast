using Grpc.Core;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>theplot's console speaks one beat of a story's world.</summary>
public sealed class BeatSpeechTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);
    private readonly Operator _theplot = new(database, "operators", "admin-theplot");

    [Fact]
    public async Task Dialogue_is_spoken_in_its_speakers_voice_and_the_rest_in_the_narrators()
    {
        var story = await _writer.CreateStoryAsync();
        var mae = await _writer.CreateCharacterAsync(story.Id, "MAE");
        const string pours = "@MAE pours a last cup for the trucker.";
        var scene = await _writer.WriteFirstSceneAsync(story,
            Heading(), Action(pours, Mention(pours, "@MAE", mae)), Dialogue(mae.Id, "On the house, trucker."));

        var action = await _theplot.Call<TheplotWorldModelsService, SpeakBeatReply>(
            (s, c) => s.SpeakBeat(new SpeakBeatRequest { StoryId = story.Id, BeatId = scene.Elements[1].Id }, c));
        var line = await _theplot.Call<TheplotWorldModelsService, SpeakBeatReply>(
            (s, c) => s.SpeakBeat(new SpeakBeatRequest { StoryId = story.Id, BeatId = scene.Elements[2].Id }, c));

        Assert.Equal(("audio/mpeg", "Narrator", 300L), (action.ContentType, action.VoiceName, action.CostUsdMicros));
        Assert.Equal($"Voice of {mae.Id}", line.VoiceName);
        Assert.Contains(("MAE pours a last cup for the trucker.", RecordingSpeechSynthesizer.Narrator), database.Voices.Lines);
        Assert.Contains(database.Voices.Lines, l => l.Text == "On the house, trucker." && l.Voice.Id == $"voice-{mae.Id}");
    }

    [Fact]
    public async Task A_beat_of_another_story_is_not_found()
    {
        var story = await _writer.CreateStoryAsync();
        var other = await _writer.CreateStoryAsync();
        var scene = await _writer.WriteFirstSceneAsync(other, Heading(), Action("Someone else's beat."));

        var speech = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotWorldModelsService, SpeakBeatReply>(
            (s, c) => s.SpeakBeat(new SpeakBeatRequest { StoryId = story.Id, BeatId = scene.Elements[1].Id }, c)));

        Assert.Equal(StatusCode.NotFound, speech.StatusCode);
    }
}
