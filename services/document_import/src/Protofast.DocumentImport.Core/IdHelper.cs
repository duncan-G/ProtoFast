using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Protofast.DocumentImport.Core;

public static class IdHelper
{
    /// <summary>
    /// A lowercase Crockford-style ULID: 48-bit millisecond timestamp then 80 random bits, so
    /// run ids sort by creation time in a Postgres index and in an S3 listing. Also the SQS
    /// message deduplication key (plan §8.4).
    /// </summary>
    public static string NewDocumentImportRunId() => NewDocumentImportRunId(DateTimeOffset.UtcNow, RandomNumberGenerator.GetBytes(10));

    private static string NewDocumentImportRunId(DateTimeOffset now, ReadOnlySpan<byte> randomness)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes, now.ToUnixTimeMilliseconds() << 16);
        randomness[..10].CopyTo(bytes[6..]);
        return Crockford(bytes);
    }

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
}
