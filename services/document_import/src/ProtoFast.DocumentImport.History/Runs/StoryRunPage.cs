namespace ProtoFast.DocumentImport.History.Runs;

public sealed record StoryRunPage(IReadOnlyList<StoryRun> Runs, int Total);
