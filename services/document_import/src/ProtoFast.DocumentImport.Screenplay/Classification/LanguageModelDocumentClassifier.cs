using System.Text;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Families;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Classification;

/// <summary>
/// Puts a document with the registered family whose description fits its synopsis, or opens a
/// new family when none does. The taxonomy is the registry's descriptions, so it grows with what
/// comes in and an operator can sharpen it from the console.
/// </summary>
public sealed class LanguageModelDocumentClassifier(
    SynopsisWriter synopses,
    IDocumentFamilyRegistry families,
    ILanguageModelFactory models,
    IArtifactStore artifacts,
    DocumentClassifierOptions options,
    TimeProvider time,
    ILogger<LanguageModelDocumentClassifier> logger) : IDocumentClassifier
{
    public const string CreatedBy = "classifier";

    private const int MaxDisplayName = 200;

    private const string System = """
        You sort documents into families. A family is a set of documents alike enough to be processed
        the same way: the same kind of document in the same form, calling for the same steps. One
        vendor's invoices are a family; a screenplay and a novel are not, since turning each into a
        story takes different work.

        You are given a synopsis of one document and the families that exist, each with a description
        of what belongs in it. Choose the family whose description fits. Open a new family only when no
        existing description fits; two families that overlap are worse than one that is slightly broad.

        Reply with one JSON object and nothing else. For an existing family:
        {"family": "<its name>", "reason": "<one sentence>"}
        For a new family:
        {"new_family": {"name": "<lowercase letters, digits, '-' and '_'; starts with a letter; at most 64 characters>", "display_name": "<a short title>", "description": "<what belongs in this family and what sets it apart from the others, written for whoever sorts the next document>"}, "reason": "<one sentence>"}
        """;

    public async Task<DocumentSignature> ClassifyAsync(ArtifactRef input, CancellationToken ct)
    {
        var text = await ArtifactText.ReadAsync(artifacts, input, ct);
        var synopsis = await synopses.WriteAsync(text, ct);
        var registered = await families.ListAsync(ct);

        var reply = await models.For(options.ModelClass).CompleteAsync(System, Prompt(synopsis, registered), ct);
        var choice = StoryJson.Deserialize<FamilyChoice>(StoryJson.ExtractObject(reply.Text));
        var (family, created) = await ResolveAsync(choice, registered, ct);
        logger.LogInformation(
            "Classified source {SourceId} as {Family}{Created} with {Model}: {Reason}",
            input.RunId, family, created ? " (new)" : "", reply.ModelId, choice.Reason);

        return new DocumentSignature(family, new Dictionary<string, string>
        {
            ["synopsis"] = synopsis.Summary,
            ["form"] = synopsis.Form ?? "",
            ["language"] = synopsis.Language ?? "",
            ["chars"] = text.Length.ToString(),
            ["classifier"] = reply.ModelId,
            ["reason"] = choice.Reason ?? "",
            ["family_created"] = created ? "true" : "false",
        });
    }

    private static string Prompt(Synopsis synopsis, IReadOnlyList<DocumentFamilyInfo> registered)
    {
        var prompt = new StringBuilder()
            .AppendLine("<document>")
            .AppendLine($"Form: {synopsis.Form}")
            .AppendLine($"Language: {synopsis.Language}")
            .AppendLine($"Structure: {synopsis.Structure}")
            .AppendLine($"Synopsis: {synopsis.Summary}")
            .AppendLine("</document>")
            .AppendLine()
            .AppendLine("<families>");
        if (registered.Count == 0)
        {
            prompt.AppendLine("None yet.");
        }

        foreach (var family in registered)
        {
            prompt.AppendLine($"- {family.Family}: {family.Description}");
        }

        return prompt.AppendLine("</families>").ToString();
    }

    private async Task<(string Family, bool Created)> ResolveAsync(
        FamilyChoice choice, IReadOnlyList<DocumentFamilyInfo> registered, CancellationToken ct)
    {
        if (choice.Family is { } named)
        {
            return registered.Any(f => f.Family == named)
                ? (named, false)
                : throw new DocumentClassificationException($"The model chose '{named}', which is not a registered family.");
        }

        if (choice.NewFamily is not { } proposed)
        {
            throw new DocumentClassificationException("The model named neither a family nor a new one.");
        }

        if (!DocumentFamilyNames.IsValid(proposed.Name))
        {
            throw new DocumentClassificationException($"The model proposed '{proposed.Name}', which is not a valid family name.");
        }

        if (string.IsNullOrWhiteSpace(proposed.Description))
        {
            throw new DocumentClassificationException($"The model proposed '{proposed.Name}' without a description.");
        }

        var displayName = string.IsNullOrWhiteSpace(proposed.DisplayName) ? proposed.Name : proposed.DisplayName.Trim();
        var now = time.GetUtcNow();
        try
        {
            await families.CreateAsync(
                new DocumentFamilyInfo(
                    proposed.Name, displayName[..Math.Min(displayName.Length, MaxDisplayName)], proposed.Description.Trim(), CreatedBy, now, now),
                ct);
            return (proposed.Name, true);
        }
        catch (InvalidOperationException)
        {
            // Another import opened it first, or the model re-proposed a family it was shown.
            return (proposed.Name, false);
        }
    }
}
