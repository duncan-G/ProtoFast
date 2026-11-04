using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using NoteKindRecord = ProtoFast.Data.OffTheLoop.Entities.NoteKind;
using NoteRecord = ProtoFast.Data.OffTheLoop.Entities.Note;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class NoteMessages
{
    public static Note ToMessage(NoteRecord note)
    {
        var message = new Note
        {
            Id = note.Id.ToString(),
            Kind = ToMessage(note.Kind),
            CompanyId = note.CompanyId?.ToString() ?? "",
            Title = note.Title,
            Tag = note.Tag,
            Body = note.Body,
            LastModifiedUnixSeconds = LoopText.UnixSeconds(note.DateLastModified),
        };
        message.Badges.AddRange(note.Badges);
        message.Fits.AddRange(note.Fits);
        return message;
    }

    /// <summary>A company note's company is checked by the caller, which can see whose it is.</summary>
    public static NoteRecord ToRecord(Note message)
    {
        var kind = ToRecord(message.Kind);
        return new NoteRecord
        {
            Kind = kind,
            CompanyId = CompanyOf(kind, message.CompanyId),
            Title = LoopText.Required(message.Title, OffTheLoopLimits.Name, "The title", "Give the note a title."),
            Tag = LoopText.Optional(message.Tag, OffTheLoopLimits.Label, "The tag"),
            Body = LoopText.Markdown(message.Body, OffTheLoopLimits.Body, "The note"),
            Badges = LoopText.Items(message.Badges, OffTheLoopLimits.Label, "Badges"),
            Fits = LoopText.Items(message.Fits, OffTheLoopLimits.Line, "What it fits"),
        };
    }

    public static void Overwrite(NoteRecord target, NoteRecord source)
    {
        target.Kind = source.Kind;
        target.CompanyId = source.CompanyId;
        target.Title = source.Title;
        target.Tag = source.Tag;
        target.Body = source.Body;
        target.Badges = source.Badges;
        target.Fits = source.Fits;
    }

    private static Guid? CompanyOf(NoteKindRecord kind, string companyId) => kind switch
    {
        NoteKindRecord.Company when Guid.TryParse(companyId, out var id) => id,
        NoteKindRecord.Company => throw LoopErrors.Invalid(LoopErrors.CompanyNotFound),
        _ when companyId.Length == 0 => null,
        _ => throw LoopErrors.Invalid("Only a company note belongs to a company."),
    };

    private static NoteKind ToMessage(NoteKindRecord kind) => kind switch
    {
        NoteKindRecord.Pattern => NoteKind.Pattern,
        NoteKindRecord.Design => NoteKind.Design,
        NoteKindRecord.Company => NoteKind.Company,
        NoteKindRecord.Story => NoteKind.Story,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static NoteKindRecord ToRecord(NoteKind kind) => kind switch
    {
        NoteKind.Pattern => NoteKindRecord.Pattern,
        NoteKind.Design => NoteKindRecord.Design,
        NoteKind.Company => NoteKindRecord.Company,
        NoteKind.Story => NoteKindRecord.Story,
        _ => throw LoopErrors.Invalid("Pick what kind of note it is."),
    };
}
