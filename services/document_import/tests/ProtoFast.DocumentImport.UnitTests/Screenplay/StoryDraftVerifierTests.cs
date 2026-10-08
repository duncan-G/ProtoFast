using System.Text;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Verifiers;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public class StoryDraftVerifierTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryArtifactStore _artifacts = new();

    [Fact]
    public async Task Names_spelled_with_curly_or_straight_quotes_are_the_same_library_name()
    {
        var verdict = await VerifyAsync("""
            {
              "title": "In the Name of the Mother",
              "characters": [{ "name": "SER ARLAN", "kind": "Human" }, { "name": "D’ARCY", "kind": "Human" }],
              "locations": [{ "name": "KING'S ROAD, CROWNLANDS", "setting": "Exterior" }],
              "containers": [{ "label": "Act I", "scenes": [{ "title": "Road", "elements": [
                { "type": "Heading", "location": "KING’S ROAD, CROWNLANDS" },
                { "type": "Dialogue", "speaker": "D'ARCY", "text": "Ride on." }
              ] }] }]
            }
            """);

        Assert.Equal((Verdict.Pass, 0), (verdict.Verdict, verdict.Findings.Count));
    }

    [Fact]
    public async Task A_name_declared_with_both_quote_styles_is_a_duplicate()
    {
        var verdict = await VerifyAsync("""
            {
              "title": "In the Name of the Mother",
              "characters": [{ "name": "SER ARLAN", "kind": "Human" }],
              "locations": [{ "name": "SER ARLAN'S CAMP" }, { "name": "SER ARLAN’S CAMP" }],
              "containers": [{ "label": "Act I", "scenes": [{ "title": "Camp", "elements": [
                { "type": "Heading", "location": "SER ARLAN'S CAMP" }
              ] }] }]
            }
            """);

        Assert.Equal(Verdict.Fail, verdict.Verdict);
        Assert.Contains(verdict.Findings, f => f.Path == "$.locations");
    }

    [Fact]
    public async Task A_speaker_that_keeps_its_cue_extension_is_told_where_the_extension_goes()
    {
        var verdict = await VerifyAsync("""
            {
              "title": "In the Name of the Mother",
              "characters": [{ "name": "ASHFORD SEPTON", "kind": "Human" }],
              "locations": [{ "name": "ASHFORD MEADOW", "setting": "Exterior" }],
              "containers": [{ "label": "Act I", "scenes": [{ "title": "Meadow", "elements": [
                { "type": "Heading", "location": "ASHFORD MEADOW" },
                { "type": "Dialogue", "speaker": "ASHFORD SEPTON (V.O.)", "text": "May the Seven bear witness." }
              ] }] }]
            }
            """);

        Assert.Equal(Verdict.Degraded, verdict.Verdict);
        Assert.Contains("the extension in extension", Assert.Single(verdict.Findings).Message);
    }

    private async Task<VerifierResult> VerifyAsync(string story)
    {
        var runId = Core.DocumentImportIds.New();
        var output = await _artifacts.PutAsync(
            runId, StoryStages.StoryStage, new MemoryStream(Encoding.UTF8.GetBytes(story)), StoryStages.StoryContract, Ct);
        var stage = new StageDefinition(
            StoryStages.StoryStage, [], StoryStages.SourceContract, StoryStages.StoryContract, [StoryDraftVerifier.VerifierId], Budget.Unbounded);
        var request = new StageRequest(runId, stage, new DocumentSignature("screenplay", new Dictionary<string, string>()), []);
        return await new StoryDraftVerifier(_artifacts).VerifyAsync(request, new StageResult(output, null, Cost.Zero, []), Ct);
    }
}
