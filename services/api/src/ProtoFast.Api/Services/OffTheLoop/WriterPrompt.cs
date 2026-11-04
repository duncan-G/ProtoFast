using System.Security;
using ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class WriterPrompt
{
    public const string System = """
        You write inside a note in OffTheLoop, where an engineer keeps what they learn while preparing for interviews: coding patterns, system design, stories for behavioural questions, and notes on each company they're interviewing with.

        The note is markdown, and the editor renders:
        - headings, lists, task lists, tables, quotes, **bold**, *italic* and `inline code`
        - fenced code blocks with a language, such as ```python
        - inline math as $O(n \log n)$, and display math as a $$ line, the LaTeX, then a closing $$ line
        - diagrams as ```mermaid blocks: flowchart, sequenceDiagram, classDiagram, stateDiagram-v2, erDiagram

        The document marks where your text goes: ⟦cursor⟧ for an insertion, or ⟦selection⟧ and ⟦/selection⟧ around the text your reply replaces. Reply with only the text that goes there: no preamble or sign-off, no fence around the whole reply, none of the surrounding document, and no markers. Match the note's voice, and keep to what the instruction asks for.
        """;

    public static string User(WriteRequest request)
    {
        var kind = request.Kind == NoteKind.Unspecified ? "" : $" kind=\"{request.Kind.ToString().ToLowerInvariant()}\"";
        var parts = new List<string>
        {
            $"<note title=\"{SecurityElement.Escape(request.Title)}\"{kind}>\n{request.Document}\n</note>",
        };
        if (request.Selection.Length > 0)
        {
            parts.Add($"<selection>\n{request.Selection}\n</selection>");
        }

        parts.Add($"<instruction>\n{request.Instruction}\n</instruction>");
        return string.Join("\n\n", parts);
    }
}
