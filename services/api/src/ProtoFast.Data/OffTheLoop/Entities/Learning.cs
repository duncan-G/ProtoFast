namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>One thing learned along the way. Owned by its <see cref="Application"/> and stored in it.</summary>
public sealed class Learning
{
    public required string Text { get; set; }

    /// <summary>The round it came from, by name; empty when it came from none.</summary>
    public string Round { get; set; } = "";

    public DateOnly? On { get; set; }
}
