using ProtoFast.DocumentImport.Engine.Briefing;

namespace ProtoFast.DocumentImport.Screenplay.Briefing;

/// <param name="Text">The step as the step model reads it.</param>
internal sealed record StepDigest(RunStep Step, bool NeedsBrief, string Text);
