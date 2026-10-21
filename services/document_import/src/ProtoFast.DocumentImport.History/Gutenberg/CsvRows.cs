using System.Text;

namespace ProtoFast.DocumentImport.History.Gutenberg;

/// <summary>RFC 4180 rows: quoted fields may hold commas, line breaks and doubled quotes.</summary>
internal static class CsvRows
{
    public static IEnumerable<IReadOnlyList<string>> Read(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var any = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            any = true;
            var ch = (char)c;
            if (quoted)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                }
                else if (reader.Peek() == '"')
                {
                    field.Append('"');
                    reader.Read();
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return fields;
                    fields = [];
                    any = false;
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (any)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}
