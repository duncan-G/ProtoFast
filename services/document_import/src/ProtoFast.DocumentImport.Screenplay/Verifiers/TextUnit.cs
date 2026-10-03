namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <param name="Path">Where in the story the text sits, as a JSON path.</param>
public sealed record TextUnit(string Path, string Text);
