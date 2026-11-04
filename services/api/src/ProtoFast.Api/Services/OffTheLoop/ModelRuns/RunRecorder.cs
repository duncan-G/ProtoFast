using System.Diagnostics;
using System.Text;
using ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>
/// A running run's progress, saved as it goes so watchers see it: each research step at once, and
/// written text as it starts, then at most every <see cref="RunOptions.ProgressEvery"/>. A save
/// that finds the run no longer running stops it.
/// </summary>
public sealed class RunRecorder(RunStore store, Guid id, CancellationTokenSource run, TimeSpan every, RunProgress start)
{
    private readonly List<ResearchStep> _steps = [.. start.Steps];
    private readonly StringBuilder _text = new(start.Text);
    private long _savedAt;

    public bool Wrote => _text.Length > 0;

    public RunProgress Progress
    {
        get
        {
            var progress = new RunProgress { Text = _text.ToString() };
            progress.Steps.AddRange(_steps);
            return progress;
        }
    }

    public Task StepAsync(ResearchStep step)
    {
        _steps.Add(step);
        return SaveAsync();
    }

    public Task TextAsync(string text)
    {
        _text.Append(text);
        return Stopwatch.GetElapsedTime(_savedAt) >= every ? SaveAsync() : Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        _savedAt = Stopwatch.GetTimestamp();
        if (!await store.RecordProgressAsync(id, Progress, run.Token))
        {
            await run.CancelAsync();
        }
    }
}
