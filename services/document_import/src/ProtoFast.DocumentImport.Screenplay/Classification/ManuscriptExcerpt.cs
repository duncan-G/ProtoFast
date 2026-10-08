namespace ProtoFast.DocumentImport.Screenplay.Classification;

/// <summary>The opening, a slice of the middle and the ending of a long text, so a synopsis sees how it starts, goes and ends.</summary>
public static class ManuscriptExcerpt
{
    public const string Cut = "\n\n[…]\n\n";

    public static string Of(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var head = maxChars / 2;
        var tail = maxChars / 4;
        var middle = maxChars - head - tail;
        var middleStart = (text.Length - middle) / 2;
        return text[..head] + Cut + text.Substring(middleStart, middle) + Cut + text[^tail..];
    }
}
