using System.Text.RegularExpressions;
using ProtoFast.DocumentImport.History.Corpus;

namespace ProtoFast.DocumentImport.History.Gutenberg;

/// <summary>
/// Project Gutenberg's catalog CSV, narrowed to American history: English texts in Library of
/// Congress class E (United States) or F1–F975 (its local history; F976 on is Canada and Latin
/// America), or on the "History - American" shelf, never literature (class P), so novels set in
/// the past stay out.
/// </summary>
public static partial class GutenbergCatalog
{
    public const string CatalogUrl = "https://www.gutenberg.org/cache/epub/feeds/pg_catalog.csv";

    private const string AmericanHistoryShelf = "Category: History - American";
    private const int LastUsLocalHistoryClass = 975;

    public static IEnumerable<CorpusBook> AmericanHistory(TextReader catalog)
    {
        using var rows = CsvRows.Read(catalog).GetEnumerator();
        if (!rows.MoveNext())
        {
            yield break;
        }

        var header = rows.Current.Select((name, i) => (name, i)).ToDictionary(h => h.name.Trim(), h => h.i);
        while (rows.MoveNext())
        {
            var row = rows.Current;
            string Field(string name) => header.TryGetValue(name, out var i) && i < row.Count ? row[i] : "";

            if (!int.TryParse(Field("Text#"), out var id)
                || !IsAmericanHistory(Field("Type"), Field("Language"), Field("LoCC"), Field("Bookshelves")))
            {
                continue;
            }

            var title = Field("Title").Replace("\r", "").Replace("\n", " — ");
            var subjects = Field("Subjects");
            var (first, last) = SubjectYears.Find(subjects, title);
            yield return new CorpusBook(
                id, title, Field("Authors"), Field("Issued"), subjects, Field("LoCC"), Field("Bookshelves"), first, last);
        }
    }

    internal static bool IsAmericanHistory(string type, string language, string locc, string bookshelves)
    {
        if (type != "Text" || !Split(language).Contains("en"))
        {
            return false;
        }

        var classes = Split(locc).ToList();
        if (classes.Any(c => c.StartsWith('P')))
        {
            return false;
        }

        foreach (var c in classes)
        {
            if (c.StartsWith('E'))
            {
                return true;
            }

            if (c.StartsWith('F'))
            {
                var m = LocalHistoryClass().Match(c);
                if (!m.Success || int.Parse(m.Groups[1].Value) <= LastUsLocalHistoryClass)
                {
                    return true;
                }
            }
        }

        return bookshelves.Contains(AmericanHistoryShelf, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Split(string list) =>
        list.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"^F(\d+)")]
    private static partial Regex LocalHistoryClass();
}
