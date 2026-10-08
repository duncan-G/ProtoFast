namespace ProtoFast.DocumentImport.Screenplay.Tagging;

/// <param name="Element">Index into the tagger's list of element texts.</param>
internal sealed record NameOccurrence(int Element, int Start, int Length, LibraryName Name);
