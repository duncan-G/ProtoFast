namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>Someone at the company. Owned by its <see cref="Company"/> and stored in it.</summary>
public sealed class Contact
{
    public required string Name { get; set; }

    public string Role { get; set; } = "";

    public DateOnly? MetOn { get; set; }

    public string Note { get; set; } = "";
}
