using System.Text.Json.Serialization;

namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <summary>Declaration order breaks ties between equal names, as the editor's autocomplete does.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MentionKind>))]
public enum MentionKind
{
    Character,
    Location,
    Prop,
}
