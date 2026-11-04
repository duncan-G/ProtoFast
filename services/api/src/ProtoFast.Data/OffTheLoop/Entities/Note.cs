using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Entities;

public sealed class Note : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public NoteKind Kind { get; set; }

    /// <summary>Set exactly when <see cref="Kind"/> is <see cref="NoteKind.Company"/>.</summary>
    public Guid? CompanyId { get; set; }

    public required string Title { get; set; }

    public string Tag { get; set; } = "";

    /// <summary>Markdown, with $math$, $$math$$ and ```mermaid diagrams.</summary>
    public string Body { get; set; } = "";

    /// <summary>Short facts under the note: "O(n) time".</summary>
    public List<string> Badges { get; set; } = [];

    /// <summary>Problems or prompts it answers.</summary>
    public List<string> Fits { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
