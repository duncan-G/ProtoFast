namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>A resume as the model gets it: its text, or the file itself when the model reads that kind.</summary>
public sealed record ResumeFile(string Text, ResearchFile? Attachment);
