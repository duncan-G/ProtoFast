using System.Text;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Screenplay.Agents;

namespace ProtoFast.DocumentImport.Screenplay.Executors;

/// <summary>The user turn for a stage: every input artifact, labelled by the stage that produced it.</summary>
public static class StagePrompt
{
    public static async Task<string> BuildAsync(
        IArtifactStore artifacts, StageRequest request, int maxSourceChars, IReadOnlyList<string>? corrections, CancellationToken ct)
    {
        var prompt = new StringBuilder(await InputsAsync(artifacts, request.Inputs, maxSourceChars, ct));
        if (corrections is { Count: > 0 })
        {
            prompt.AppendLine("A previous attempt was rejected for these reasons; fix every one of them:");
            foreach (var correction in corrections)
            {
                prompt.AppendLine($"- {correction}");
            }

            prompt.AppendLine();
        }

        prompt.AppendLine($"Produce the {request.Stage.Output.SchemaId} document now.");
        return prompt.ToString();
    }

    public static async Task<string> InputsAsync(
        IArtifactStore artifacts, IReadOnlyList<ArtifactRef> inputs, int maxSourceChars, CancellationToken ct)
    {
        var prompt = new StringBuilder();
        foreach (var input in inputs)
        {
            var text = await ArtifactText.ReadAsync(artifacts, input, ct);
            if (input.StageId == ArtifactRef.InputStageId)
            {
                if (text.Length > maxSourceChars)
                {
                    text = text[..maxSourceChars] + "\n\n[The manuscript was cut here.]";
                }

                prompt.AppendLine("<manuscript>").AppendLine(text).AppendLine("</manuscript>");
            }
            else
            {
                prompt.AppendLine($"<{input.StageId}>").AppendLine(text).AppendLine($"</{input.StageId}>");
            }

            prompt.AppendLine();
        }

        return prompt.ToString();
    }
}
