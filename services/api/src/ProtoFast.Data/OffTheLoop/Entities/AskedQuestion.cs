namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>A question put to the company, with what they said. Owned by its <see cref="Application"/> and stored in it.</summary>
public sealed class AskedQuestion
{
    public required string Question { get; set; }

    public string Answer { get; set; } = "";
}
