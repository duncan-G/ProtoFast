using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Classification;

/// <summary>One small-model call that says what a document is and what it contains, for the classifier and the ledger.</summary>
public sealed class SynopsisWriter(
    ILanguageModelFactory models,
    DocumentClassifierOptions options,
    ILogger<SynopsisWriter> logger)
{
    private const string System = """
        You summarise documents for a classifier that groups similar documents together. You are shown
        the whole document, or its opening, a slice of its middle and its ending when it is long.

        Reply with one JSON object and nothing else:
        {
          "summary": "What the document is and what it contains, in about 150 words. For a narrative: premise, principal characters, setting and arc. For anything else: purpose, subject and the shape of its content.",
          "form": "The kind of document, in a few words: feature screenplay, stage play, novel manuscript, short story, treatment, outline, meeting notes…",
          "language": "The language the document is written in.",
          "structure": "How the text is laid out, in one sentence: scene headings, dialogue, chapters, headed sections, lists, tables."
        }
        """;

    public async Task<Synopsis> WriteAsync(string text, CancellationToken ct)
    {
        var excerpt = ManuscriptExcerpt.Of(text, options.MaxExcerptChars);
        var user = excerpt.Length < text.Length
            ? $"<excerpt total-characters=\"{text.Length}\">\n{excerpt}\n</excerpt>"
            : $"<document>\n{text}\n</document>";

        var reply = await models.For(options.SynopsisModelClass).CompleteAsync(System, user, ct);
        logger.LogInformation(
            "Synopsis by {Model}: {In} in, {Out} out", reply.ModelId, reply.InputTokens, reply.OutputTokens);

        var synopsis = StoryJson.Deserialize<Synopsis>(StoryJson.ExtractObject(reply.Text));
        if (string.IsNullOrWhiteSpace(synopsis.Summary))
        {
            throw new DocumentClassificationException("The synopsis model returned no summary.");
        }

        return synopsis;
    }
}
