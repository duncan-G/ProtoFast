using System.Diagnostics;
using ProtoFast.Segmentation.Core.Observability;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// Span failure marking. A span left unset renders as "fine" in every tracing backend, so these
/// pin the behaviour that stops a failed run from showing green.
/// </summary>
public class ActivityFailureTests
{
    private static readonly ActivitySource Source;

    /// <summary>
    /// An explicit static constructor, and the listener registered from inside it, so the source
    /// exists before the listener goes looking for it. Registering from an instance constructor
    /// instead lets <c>ShouldListenTo</c> be the first thing to touch <c>Source</c> — which
    /// triggers its lazy initialization midway through the registration that was supposed to
    /// attach to it, and the first span of the run comes back unsampled and null.
    /// </summary>
    static ActivityFailureTests()
    {
        Source = new ActivitySource("ProtoFast.Segmentation.Tests");

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source == Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        });
    }

    [Fact]
    public void AnExceptionSetsTheStatusTheTypeTagAndAStackBearingEvent()
    {
        using var activity = Source.StartActivity("phase");
        Assert.NotNull(activity);

        activity.Fail(Boom("the structurer could not produce a valid tree"));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("the structurer could not produce a valid tree", activity.StatusDescription);
        Assert.Equal("System.InvalidOperationException", activity.GetTagItem("error.type"));

        // The stack is the part that was missing from the logs; on the span it travels as an
        // exception event, so a trace can answer "what threw" without a log correlation.
        var recorded = Assert.Single(activity.Events);
        Assert.Equal("exception", recorded.Name);
        Assert.Contains(
            recorded.Tags,
            tag => tag.Key == "exception.stacktrace" && tag.Value?.ToString()?.Contains("Boom") == true);
    }

    [Fact]
    public void TheFirstFailureWinsBecauseItIsTheOneNearestTheCause()
    {
        using var activity = Source.StartActivity("run");
        Assert.NotNull(activity);

        activity.Fail(Boom("no eligible model had headroom"));
        activity.Fail("run.failed", "The run failed and will be redelivered.");

        // The outer layer only knows the run failed; overwriting would trade the cause for a
        // restatement of the symptom.
        Assert.Equal("no eligible model had headroom", activity.StatusDescription);
        Assert.Single(activity.Events);
    }

    [Fact]
    public void AFailureWithNoExceptionStillColoursTheSpan()
    {
        using var activity = Source.StartActivity("run");
        Assert.NotNull(activity);

        activity.Fail("workflow.failed", "The run stopped without reporting a reason.");

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("workflow.failed", activity.GetTagItem("error.type"));
        Assert.Empty(activity.Events);
    }

    [Fact]
    public void AnUnsampledSpanIsANoOpRatherThanANullReference()
    {
        // Every call site holds an Activity? — StartActivity returns null when nothing is
        // listening, which is the normal case in production for an unsampled trace.
        Activity? none = null;

        none.Fail(Boom("boom"));
        none.Fail("run.failed", "boom");
    }

    private static Exception Boom(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException caught)
        {
            // Thrown and caught so it carries a real stack; a constructed exception has none.
            return caught;
        }
    }
}
