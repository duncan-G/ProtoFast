using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Grpc.Core;
using ProtoFast.Storage.Abstractions;
using DocumentRecord = ProtoFast.Data.OffTheLoop.Entities.Document;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Gemini reads a PDF itself, scans included; Word and OpenDocument files go as the text in their XML.</summary>
public sealed class ResumeFiles(IObjectStore objectStore)
{
    private const int MaxText = 200_000;

    // A zip's entry can inflate far past the 10 MB file.
    private const long MaxXmlBytes = 50 * 1024 * 1024;

    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace OpenText = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    public async Task<ResumeFile> ReadAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(document.StorageKey).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx" or ".odt" or ".rtf" or ".txt" or ".md"))
        {
            throw Failed(LoopErrors.ResumeFileFormat);
        }

        await using var stream = await objectStore.OpenReadAsync(document.StorageKey, cancellationToken)
                                 ?? throw Failed(LoopErrors.DocumentNotArrived);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (extension == ".pdf")
        {
            return new ResumeFile("", new ResearchFile(bytes, "application/pdf"));
        }

        string text;
        try
        {
            text = extension switch
            {
                ".docx" => Paragraphs(bytes, "word/document.xml", Word + "p", WordRun),
                ".odt" => Paragraphs(bytes, "content.xml", OpenText + "p", OpenTextRun, OpenText + "h"),
                _ => Encoding.UTF8.GetString(bytes),
            };
        }
        catch (Exception e) when (e is InvalidDataException or XmlException or KeyNotFoundException)
        {
            throw Failed(LoopErrors.ResumeUnreadable);
        }

        text = text.Trim();
        return text.Length > 0 ? new ResumeFile(text[..Math.Min(text.Length, MaxText)], null) : throw Failed(LoopErrors.ResumeUnreadable);
    }

    /// <summary>A line per paragraph; a paragraph inside another (a table's cell) is read with the outer one.</summary>
    private static string Paragraphs(byte[] zip, string entryName, XName paragraph, Func<XElement, string> run, XName? heading = null)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) ?? throw new KeyNotFoundException(entryName);
        if (entry.Length > MaxXmlBytes)
        {
            throw new InvalidDataException($"{entryName} is {entry.Length} bytes.");
        }

        using var content = entry.Open();
        var xml = XDocument.Load(content);
        var lines = xml.Descendants()
            .Where(e => e.Name == paragraph || e.Name == heading)
            .Where(e => !e.Ancestors().Any(a => a.Name == paragraph || a.Name == heading))
            .Select(run);
        return string.Join('\n', lines);
    }

    private static string WordRun(XElement paragraph)
    {
        var text = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            if (element.Name == Word + "t")
            {
                text.Append(element.Value);
            }
            else if (element.Name == Word + "tab")
            {
                text.Append('\t');
            }
            else if (element.Name == Word + "br" || element.Name == Word + "cr")
            {
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    private static string OpenTextRun(XElement paragraph)
    {
        var text = new StringBuilder();
        foreach (var node in paragraph.Nodes())
        {
            switch (node)
            {
                case XText run:
                    text.Append(run.Value);
                    break;
                case XElement { Name.LocalName: "s" } spaces:
                    text.Append(' ', int.TryParse(spaces.Attribute(OpenText + "c")?.Value, out var count) ? count : 1);
                    break;
                case XElement { Name.LocalName: "tab" }:
                    text.Append('\t');
                    break;
                case XElement { Name.LocalName: "line-break" }:
                    text.Append('\n');
                    break;
                case XElement inner:
                    text.Append(OpenTextRun(inner));
                    break;
            }
        }

        return text.ToString();
    }

    private static RpcException Failed(string message) => new(new Status(StatusCode.FailedPrecondition, message));
}
