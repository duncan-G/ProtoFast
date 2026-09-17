using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProtoFast.Segmentation.Core.Model;

/// <summary>
/// The only place identifiers are minted. Models never invent ids — anything in model output
/// that was not issued here is an <c>id-coverage</c> validation error (plan §8.5), which is
/// only enforceable because the format is fixed and checked in one place.
/// </summary>
public static partial class Ids
{
    public const string LinePrefix = "L";
    public const string ParagraphPrefix = "P";
    public const string SectionPrefix = "S";

    /// <summary>Zero-padded and monotonic: <c>L000412</c>.</summary>
    public static string Line(int index) => LinePrefix + index.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary><c>P00031</c>. Split children get a letter suffix via <see cref="SplitChild"/>.</summary>
    public static string Paragraph(int index) => ParagraphPrefix + index.ToString("D5", CultureInfo.InvariantCulture);

    /// <summary><c>S0007</c>.</summary>
    public static string Section(int index) => SectionPrefix + index.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>P00031 → P00031a, P00031b</c>. A split records the parent in
    /// <see cref="Paragraph.Lineage"/>; the <c>edit-lineage</c> check walks it.
    /// </summary>
    public static string SplitChild(string parentParagraphId, int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ordinal, 25);
        return parentParagraphId + (char)('a' + ordinal);
    }

    public static bool IsLineId(string value) => LineIdPattern().IsMatch(value);

    public static bool IsParagraphId(string value) => ParagraphIdPattern().IsMatch(value);

    public static bool IsSectionId(string value) => SectionIdPattern().IsMatch(value);

    /// <summary>
    /// A lowercase Crockford-style ULID: 48-bit millisecond timestamp then 80 random bits, so
    /// run ids sort by creation time in a Postgres index and in an S3 listing. Also the SQS
    /// message deduplication key (plan §8.4).
    /// </summary>
    public static string NewRunId() => NewRunId(DateTimeOffset.UtcNow, RandomNumberGenerator.GetBytes(10));

    internal static string NewRunId(DateTimeOffset now, ReadOnlySpan<byte> randomness)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes, now.ToUnixTimeMilliseconds() << 16);
        randomness[..10].CopyTo(bytes[6..]);
        return Crockford(bytes);
    }

    /// <summary>SHA-256, lowercase hex. Used for paragraph content hashes and prompt versions.</summary>
    public static string Sha256Hex(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Sha256Hex(ReadOnlySpan<byte> value) => Convert.ToHexStringLower(SHA256.HashData(value));

    private const string CrockfordAlphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    private static string Crockford(ReadOnlySpan<byte> bytes)
    {
        // 128 bits -> 26 base-32 characters, high bits first.
        Span<char> chars = stackalloc char[26];
        var value = new System.Numerics.BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        for (var i = 25; i >= 0; i--)
        {
            chars[i] = CrockfordAlphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(chars);
    }

    [GeneratedRegex(@"^L\d{6}$")]
    private static partial Regex LineIdPattern();

    [GeneratedRegex(@"^P\d{5}[a-z]*$")]
    private static partial Regex ParagraphIdPattern();

    [GeneratedRegex(@"^S\d{4}[a-z]*$")]
    private static partial Regex SectionIdPattern();
}
