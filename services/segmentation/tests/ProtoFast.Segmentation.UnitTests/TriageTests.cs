using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// Triage decides what the run costs: every block it leaves trusted is a block no model ever
/// sees. These tests pin both directions — it must not send clean text to a model, and it must
/// not leave genuinely structureless text alone.
/// </summary>
public class TriageTests
{
    [Fact]
    public void CleanMarkdownSkipsLabelingEntirely()
    {
        const string markdown = """
            # Introduction

            The first paragraph is well punctuated and of ordinary length.

            The second paragraph likewise reads as normal prose with sentences.
            """;

        var run = Fixtures.Run(markdown);

        Assert.True(run.Triage.CanSkipLabeling);
        Assert.Equal(ConditionBucket.Clean, run.Triage.Condition);
    }

    [Fact]
    public void AnOversizedBlockIsSuspectEvenWhenItIsWellPunctuated()
    {
        // The oversize test is relative to the document's own median block, so the document needs
        // enough ordinary blocks for that median to mean something — one normal paragraph and one
        // wall of text would put the median halfway between them and flag neither.
        var normal = Enumerable.Range(0, 12)
            .Select(i => $"Paragraph {i} is an ordinary length and reads as prose.");
        var wall = string.Join(' ', Enumerable.Range(0, 90)
            .Select(i => $"Sentence {i} of a very long but perfectly punctuated block."));

        var run = Fixtures.Run(string.Join("\n\n", normal.Append(wall)));

        Assert.False(run.Triage.CanSkipLabeling);
        Assert.Contains(
            run.Triage.SuspectRegions,
            r => r.Reasons.Any(x => x.StartsWith("oversize", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            run.Triage.SuspectRegions,
            r => r.Reasons.Contains("low-punctuation"));
    }

    [Fact]
    public void UnpunctuatedTextIsSuspect()
    {
        var run = Fixtures.Run(string.Join(' ', Enumerable.Repeat("token", 120)));

        Assert.Contains(run.Triage.SuspectRegions, r => r.Reasons.Contains("low-punctuation"));
    }

    [Fact]
    public void AShortCaptionWithNoFullStopIsNotSuspect()
    {
        // The punctuation rate only means anything over enough words; a six-word caption with no
        // full stop is not evidence of a conversion problem.
        var run = Fixtures.Run("Figure 1 shows the calibration curve\n\nAnother short caption line\n");

        Assert.DoesNotContain(run.Triage.SuspectRegions, r => r.Reasons.Contains("low-punctuation"));
    }

    [Fact]
    public void PastTheThresholdTheWholeDocumentBecomesOneSuspectRegion()
    {
        var run = Fixtures.Run(string.Join(' ', Enumerable.Repeat("token", 600)));

        Assert.True(run.Triage.WholeDocumentSuspect);
        Assert.Single(run.Triage.SuspectRegions);
        Assert.Contains("whole-document", run.Triage.SuspectRegions[0].Reasons);
        Assert.Equal(ConditionBucket.Degraded, run.Triage.Condition);
    }

    [Fact]
    public void ATranscriptIsBucketedAsConversational()
    {
        var turns = Enumerable.Range(0, 20)
            .Select(i => i % 2 == 0 ? $"ALICE: Turn {i} of the conversation." : $"BOB: Reply {i} to that point.");

        var run = Fixtures.Run(string.Join("\n\n", turns));

        Assert.Equal(ConditionBucket.Conversational, run.Triage.Condition);
    }
}
