using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

/// <summary>The two ends of a manuscript import; the agent discovers the stages between them.</summary>
public static class StoryStages
{
    public const string StoryStage = "story";

    public static readonly ContractRef SourceContract = new("document-text", 1);
    public static readonly ContractRef StoryContract = new("story-draft", 1);
}
