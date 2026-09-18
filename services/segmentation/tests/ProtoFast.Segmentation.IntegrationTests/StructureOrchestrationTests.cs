using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Pipeline.Executors;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// The orchestration loop end to end, with scripted agents and no provider (orchestrator plan
/// §10 step 8).
///
/// <para>The loop is ours now rather than a framework's, which means its round accounting, its
/// termination and its fallback are ours to get wrong. These tests are the reason that is an
/// acceptable trade: every branch of it is reachable from a string.</para>
/// </summary>
public class StructureOrchestrationTests
{
    [Fact]
    public async Task TheOrchestratorAssemblesTheWindowsIntoOneTree()
    {
        var harness = new Harness();
        harness.Router
            .Reply(AgentRole.StructureWindower, Window(["P00000", "P00001"]), Window(["P00002", "P00003"]))
            .Reply(AgentRole.StructureOrchestrator, Outline(
                (1, ["W00000:n1"], ""),
                (1, ["W00001:n1"], "")));

        var result = await harness.RunAsync();

        Assert.True(result.Success, result.Failure);
        Assert.Equal(
            ["P00000", "P00001", "P00002", "P00003"],
            result.Root!.Descend().SelectMany(n => n.ParagraphIds));

        Assert.Equal(2, harness.Router.CallsFor(AgentRole.StructureWindower));
        Assert.Equal(1, harness.Router.CallsFor(AgentRole.StructureOrchestrator));
    }

    [Fact]
    public async Task EachWindowLeavesAnArtifactAndAResumedRunReusesIt()
    {
        // The property that makes a mid-run deploy cheap (orchestrator plan §6): the second run
        // pays for the orchestrator again — the conversation is deliberately not checkpointed —
        // and for no window at all.
        var harness = new Harness();
        harness.Router
            .Reply(AgentRole.StructureWindower, Window(["P00000", "P00001"]), Window(["P00002", "P00003"]))
            .Reply(AgentRole.StructureOrchestrator, Outline(
                (1, ["W00000:n1"], ""),
                (1, ["W00001:n1"], "")));

        await harness.RunAsync();

        Assert.NotNull(await harness.Artifacts.ReadStructureWindowAsync("run-1", 0, TestContext.Current.CancellationToken));
        Assert.NotNull(await harness.Artifacts.ReadStructureWindowAsync("run-1", 1, TestContext.Current.CancellationToken));

        var second = await harness.RunAsync();

        Assert.True(second.Success, second.Failure);
        Assert.Equal(2, harness.Router.CallsFor(AgentRole.StructureWindower));
        Assert.Equal(2, harness.Router.CallsFor(AgentRole.StructureOrchestrator));
    }

    [Fact]
    public async Task AFollowUpRoundAsksTheWindowsAndThenAssembles()
    {
        var harness = new Harness();
        harness.Router
            .Reply(
                AgentRole.StructureWindower,
                Window(["P00000", "P00001"]),
                Window(["P00002", "P00003"]),
                """{"answers":[{"node":"W00001:n1","kind":"continues_previous","verdict":"yes","evidence":"P00001"}]}""")
            .Reply(
                AgentRole.StructureOrchestrator,
                """{"outline":[],"followUps":[{"kind":"continues_previous","node":"W00001:n1","relatedNode":""}],"gaps":[]}""",
                Outline((1, ["W00000:n1", "W00001:n1"], "")));

        var result = await harness.RunAsync();

        Assert.True(result.Success, result.Failure);

        // The merge the splice cannot do: one section, both windows' paragraphs, in order.
        var section = Assert.Single(result.Root!.Children);
        Assert.Equal(["P00000", "P00001", "P00002", "P00003"], section.ParagraphIds);

        // Two structuring calls plus one answering call.
        Assert.Equal(3, harness.Router.CallsFor(AgentRole.StructureWindower));
        Assert.Equal(2, harness.Router.CallsFor(AgentRole.StructureOrchestrator));

        Assert.Contains(harness.Router.Calls, c => c.Unit == "follow-up:1");
    }

    [Fact]
    public async Task AnOrchestratorThatOnlyEverAsksRunsOutOfRoundsAndReportsWhy()
    {
        // The caller's answer to this is the chunked splice — which is what phase 5 does today, so
        // the fallback is never worse than the status quo (orchestrator plan §11).
        var harness = new Harness(rounds: 2);
        harness.Router
            .Reply(
                AgentRole.StructureWindower,
                Window(["P00000", "P00001"]),
                Window(["P00002", "P00003"]),
                """{"answers":[]}""")
            .Reply(
                AgentRole.StructureOrchestrator,
                """{"outline":[],"followUps":[{"kind":"boundary_check","node":"W00000:n1","relatedNode":""}],"gaps":[]}""");

        var result = await harness.RunAsync();

        Assert.False(result.Success);
        Assert.NotNull(result.Failure);
        Assert.Null(result.Root);

        // The transcript is written even when the run failed — a failed run is exactly the one
        // whose transcript is worth reading.
        Assert.NotEmpty(result.Transcript);
    }

    [Fact]
    public async Task APlanThatOrphansAParagraphIsRepairedFromTheExactError()
    {
        var harness = new Harness();
        harness.Router
            .Reply(AgentRole.StructureWindower, Window(["P00000", "P00001"]), Window(["P00002", "P00003"]))
            .Reply(
                AgentRole.StructureOrchestrator,
                Outline((1, ["W00000:n1"], "")),
                Outline((1, ["W00000:n1"], ""), (1, ["W00001:n1"], "")));

        var result = await harness.RunAsync();

        Assert.True(result.Success, result.Failure);

        // Both orchestrator calls are the same round: the repair happens inside AgentRunner, so
        // it draws on the repair budget rather than on the round budget.
        Assert.Equal(2, harness.Router.CallsFor(AgentRole.StructureOrchestrator));
        Assert.Equal(2, result.Transcript.Count(t => t.Role == "orchestrator") + 1);
    }

    [Fact]
    public async Task ASingleWindowSkipsTheOrchestratorEntirely()
    {
        // A bench of one has nothing to assemble (orchestrator plan §8). Paying a Large call to be
        // told so would make the orchestrated strategy strictly more expensive than the chunked
        // one on the documents where they agree.
        var harness = new Harness(paragraphsPerWindow: 4, windows: 1);
        harness.Router.Reply(AgentRole.StructureWindower, Window(["P00000", "P00001", "P00002", "P00003"]));

        var result = await harness.RunAsync();

        Assert.True(result.Success, result.Failure);
        Assert.Equal(0, harness.Router.CallsFor(AgentRole.StructureOrchestrator));
        Assert.Equal(["P00000", "P00001", "P00002", "P00003"], result.Root!.Descend().SelectMany(n => n.ParagraphIds));
    }

    [Fact]
    public async Task GapsSurviveARoundThatOnlyAskedQuestions()
    {
        var harness = new Harness();
        harness.Router
            .Reply(
                AgentRole.StructureWindower,
                Window(["P00000", "P00001"]),
                Window(["P00002", "P00003"]),
                """{"answers":[]}""")
            .Reply(
                AgentRole.StructureOrchestrator,
                """
                {"outline":[],
                 "followUps":[{"kind":"title_source","node":"W00001:n1","relatedNode":""}],
                 "gaps":[{"kind":"deterministic","evidence":["W00000:n1","W00001:n1"],
                          "observation":"both fragments open with the prefix 4.2",
                          "proposal":"a numbering-prefix rule in SkeletonBuilder"}]}
                """,
                Outline((1, ["W00000:n1"], ""), (1, ["W00001:n1"], "")));

        var result = await harness.RunAsync();

        Assert.True(result.Success, result.Failure);

        var gap = Assert.Single(result.Gaps);
        Assert.Equal("deterministic", gap.Kind);
    }

    /// <summary>A window reply: one section holding the given paragraphs.</summary>
    private static string Window(string[] paragraphIds) =>
        $$"""
        {"tree":{"title":"Section","inferred":true,
                 "children":[{"title":"Part","inferred":true,
                              "paragraphs":[{{string.Join(", ", paragraphIds.Select(id => $"\"{id}\""))}}]}]},
         "paragraphEdits":[],"openQuestions":[]}
        """;

    private static string Outline(params (int Depth, string[] Sources, string Title)[] rows) =>
        $$"""
        {"outline":[{{string.Join(",", rows.Select(r =>
            $$"""{"depth":{{r.Depth}},"sources":[{{string.Join(", ", r.Sources.Select(s => $"\"{s}\""))}}],"title":"{{r.Title}}"}"""))}}],
         "followUps":[],"gaps":[]}
        """;

    private sealed class Harness
    {
        private readonly StructureOrchestration _orchestration;
        private readonly List<SkeletonBuilder.SkeletonEntry> _entries;

        public ScriptedRouter Router { get; } = new();

        public RunArtifacts Artifacts { get; }

        public Harness(int rounds = 4, int paragraphsPerWindow = 2, int windows = 2)
        {
            var store = new InMemoryArtifactStore();
            Artifacts = new RunArtifacts(store);

            var options = Options.Create(new PipelineOptions
            {
                Structure = new StructureOptions
                {
                    Strategy = StructureStrategy.Orchestrated,
                    MaxOrchestratorRounds = rounds,
                    WindowOverlapEntries = 1,
                    FanOutBatchSize = 8,
                },
            });

            var assets = new PromptAssets();
            var runner = new AgentRunner(Router, assets, NullLogger<AgentRunner>.Instance);

            var bench = new WindowBench(
                new StructureWindowerAgent(runner, options, NullLogger<StructureWindowerAgent>.Instance),
                Artifacts,
                new PhaseGate(store),
                assets,
                options,
                NullLogger<WindowBench>.Instance);

            _orchestration = new StructureOrchestration(
                bench,
                new StructureOrchestratorAgent(runner, options),
                options,
                NullLogger<StructureOrchestration>.Instance);

            // A skeleton the planner cuts into exactly `windows` windows: each opens with a
            // level-1 heading, so the cut points are the headings rather than the entry cap.
            _entries = [];
            var paragraph = 0;
            for (var w = 0; w < windows; w++)
            {
                _entries.Add(Heading(w));
                for (var p = 0; p < paragraphsPerWindow; p++)
                {
                    _entries.Add(Paragraph(paragraph++));
                }
            }
        }

        public Task<OrchestrationResult> RunAsync() =>
            _orchestration.RunAsync(
                _entries,
                [.. _entries.Where(e => e.IsHeading)
                    .Select(e => new HeadingRecord(e.Id, e.Excerpt, 1, 1, BoundarySource.Deterministic, null))],
                new StructureContext(
                    "run-1", "doc-1", "Report", "unknown", Sensitivity.Internal,
                    Instincts: [], PinnedStructurerKey: null, PinnedLevelerKey: null),
                progress: null,
                TestContext.Current.CancellationToken);

        private static SkeletonBuilder.SkeletonEntry Heading(int index) => new(
            IsHeading: true, Id: Ids.Line(index), Level: 1, Confidence: 1,
            Source: BoundarySource.Deterministic, WordCount: 2, Kind: ParagraphKind.Body,
            Excerpt: $"Heading {index}");

        private static SkeletonBuilder.SkeletonEntry Paragraph(int index) => new(
            IsHeading: false, Id: Ids.Paragraph(index), Level: null, Confidence: 1,
            Source: BoundarySource.Deterministic, WordCount: 20, Kind: ParagraphKind.Body,
            Excerpt: $"paragraph {index}");
    }
}
