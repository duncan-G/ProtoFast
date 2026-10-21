namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <param name="Summary">One line for the run's trail in the console.</param>
public sealed record ToolOutcome(string Content, bool IsError, string Summary)
{
    public static ToolOutcome Error(string message) => new(message, true, message);
}
