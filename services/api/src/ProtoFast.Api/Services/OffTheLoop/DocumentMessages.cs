using ProtoFast.Data.OffTheLoop;
using DocumentKindRecord = ProtoFast.Data.OffTheLoop.Entities.DocumentKind;
using DocumentRecord = ProtoFast.Data.OffTheLoop.Entities.Document;
using Loop = ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class DocumentMessages
{
    public static Loop.Document ToMessage(DocumentRecord document) => new()
    {
        Id = document.Id.ToString(),
        Kind = document.Kind switch
        {
            DocumentKindRecord.Resume => Loop.DocumentKind.Resume,
            DocumentKindRecord.Cv => Loop.DocumentKind.Cv,
            _ => throw new ArgumentOutOfRangeException(nameof(document), document.Kind, null),
        },
        Name = document.Name,
        FileName = document.FileName,
        MediaType = document.MediaType,
        SizeBytes = document.SizeBytes,
        UploadedUnixSeconds = LoopText.UnixSeconds(document.DateCreated),
    };

    public static DocumentKindRecord ToRecord(Loop.DocumentKind kind) => kind switch
    {
        Loop.DocumentKind.Resume => DocumentKindRecord.Resume,
        Loop.DocumentKind.Cv => DocumentKindRecord.Cv,
        _ => throw LoopErrors.Invalid("Say whether it’s a resume or a CV."),
    };

    public static string Name(string? name) =>
        LoopText.Required(name, OffTheLoopLimits.Label, "The version’s name", "Name the version, e.g. v4-backend.");

    /// <summary>"Jane Doe - v4.pdf" is named "Jane Doe - v4", cut to fit.</summary>
    public static string NameFrom(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        return (stem.Length > OffTheLoopLimits.Label ? stem[..OffTheLoopLimits.Label] : stem).TrimEnd() is { Length: > 0 } name
            ? name
            : fileName.Trim();
    }
}
