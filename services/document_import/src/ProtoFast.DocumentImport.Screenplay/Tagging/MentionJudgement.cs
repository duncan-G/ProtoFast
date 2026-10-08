namespace ProtoFast.DocumentImport.Screenplay.Tagging;

/// <param name="References">Ids of the candidates that name the prop.</param>
public sealed record MentionJudgement(IReadOnlyList<int>? References);
