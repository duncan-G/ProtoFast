using System.Globalization;
using ProtoFast.Data.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Checks what a user typed against the column it goes into.</summary>
public static class LoopText
{
    private const string DayFormat = "yyyy-MM-dd";

    public static string Required(string? value, int max, string field, string missing)
    {
        var text = Optional(value, max, field);
        return text.Length > 0 ? text : throw LoopErrors.Invalid(missing);
    }

    public static string Optional(string? value, int max, string field) => Fit((value ?? "").Trim(), max, field);

    /// <summary>Leading whitespace is indentation.</summary>
    public static string Markdown(string? value, int max, string field) =>
        Fit((value ?? "").TrimEnd().TrimStart('\r', '\n'), max, field);

    /// <summary>Only http(s), since the client renders it as a link.</summary>
    public static string Url(string? value, int max, string field)
    {
        var text = Optional(value, max, field);
        return text.Length == 0
               || (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            ? text
            : throw LoopErrors.Invalid($"{field} isn’t a web address.");
    }

    /// <summary>A page's site, as people name it: "boards.greenhouse.io".</summary>
    public static string Site(Uri uri) =>
        uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;

    /// <summary>Blank entries are dropped.</summary>
    public static List<string> Items(IEnumerable<string> values, int max, string field)
    {
        var items = values.Select(v => Optional(v, max, field)).Where(v => v.Length > 0).ToList();
        return items.Count <= OffTheLoopLimits.ListItems
            ? items
            : throw LoopErrors.Invalid($"{field} can hold at most {OffTheLoopLimits.ListItems} entries.");
    }

    public static DateOnly Day(string? value, string field) =>
        DateOnly.TryParseExact(value, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw LoopErrors.Invalid($"{field} isn’t a date.");

    public static DateOnly? OptionalDay(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : Day(value, field);

    public static string Format(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);

    public static long UnixSeconds(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static string Fit(string text, int max, string field) =>
        text.Length <= max ? text : throw LoopErrors.Invalid($"{field} can be at most {max} characters.");
}
