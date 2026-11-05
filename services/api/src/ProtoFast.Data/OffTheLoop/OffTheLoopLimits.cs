namespace ProtoFast.Data.OffTheLoop;

/// <summary>Column lengths, which the API checks before a write reaches them.</summary>
public static class OffTheLoopLimits
{
    public const int Name = 200;

    public const int Line = 300;

    public const int Label = 80;

    public const int Text = 4000;

    /// <summary>A couple of plain sentences.</summary>
    public const int Summary = 600;

    /// <summary>A note's page, diagrams and code included.</summary>
    public const int Body = 50000;

    public const int Url = 2048;

    public const int FileName = 255;

    /// <summary>Room for a pasted job description.</summary>
    public const int Posting = 20000;

    public const int ListItems = 30;

    /// <summary>Things learned on one application.</summary>
    public const int Learnings = 200;

    /// <summary>Jobs, projects or schools on one resume.</summary>
    public const int ResumeEntries = 50;
}
