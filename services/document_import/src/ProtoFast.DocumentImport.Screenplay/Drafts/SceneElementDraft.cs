namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <param name="Type">Heading, Action, Description, Narration, Dialogue or Transition.</param>
/// <param name="Location">Heading only: a library location name.</param>
/// <param name="TimeOfDay">Heading only, e.g. DAY or NIGHT.</param>
/// <param name="Speaker">Dialogue only: a library character name.</param>
/// <param name="Transition">Transition only, e.g. CUT TO.</param>
public sealed record SceneElementDraft(
    string Type,
    string? Text,
    string? Location,
    string? TimeOfDay,
    string? Speaker,
    string? Parenthetical,
    string? Transition);
