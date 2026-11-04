namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>A step of the company's process, in order. Owned by its <see cref="Application"/> and stored in it.</summary>
public sealed class Round
{
    public required string Label { get; set; }

    public RoundState State { get; set; }
}
