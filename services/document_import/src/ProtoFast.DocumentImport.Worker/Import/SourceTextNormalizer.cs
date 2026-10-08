using System.Text;

namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>
/// Makes a source's text storable and stable before any run reads it: NFC, \n line breaks, every
/// apostrophe and single quote as <c>'</c>, NUL as a space (PDF fonts that map their space glyph to
/// U+0000) and other control characters dropped. Form feeds stay because family scripts read them as
/// page breaks.
/// </summary>
public static class SourceTextNormalizer
{
    public static string Normalize(string text)
    {
        var normalized = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\r':
                    normalized.Append('\n');
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case '\0':
                    normalized.Append(' ');
                    break;
                case '‘' or '’' or '‚' or '‛' or 'ʼ':
                    normalized.Append('\'');
                    break;
                case '\t' or '\n' or '\f':
                    normalized.Append(text[i]);
                    break;
                case var c when !char.IsControl(c):
                    normalized.Append(c);
                    break;
            }
        }

        return normalized.ToString().Normalize(NormalizationForm.FormC);
    }
}
