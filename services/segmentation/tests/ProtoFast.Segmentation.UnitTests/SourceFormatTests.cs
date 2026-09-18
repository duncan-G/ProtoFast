using ProtoFast.Segmentation.Core.Ingest;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// The upload allowlist (ingest plan §6, §24).
///
/// <para>Two properties matter more than the rest of the table. The extension that ends up in the
/// S3 key comes from here and never from the uploaded filename — that is what keeps a crafted
/// name out of the key and out of the signed policy. And a media type that contradicts the
/// extension is refused, because the pair is what the policy is signed for.</para>
/// </summary>
public class SourceFormatTests
{
    [Theory]
    [InlineData("paper.md", "text/markdown", ".md", false)]
    [InlineData("notes.txt", "text/plain", ".txt", false)]
    [InlineData("report.pdf", "application/pdf", ".pdf", true)]
    [InlineData("deck.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx", true)]
    [InlineData("scan.PNG", "image/png", ".png", true)]
    public void AnAllowedUploadResolvesToItsCanonicalFormat(
        string fileName, string mediaType, string expectedExtension, bool requiresConversion)
    {
        Assert.True(SourceFormats.TryResolve(fileName, mediaType, out var format));
        Assert.Equal(expectedExtension, format.Extension);
        Assert.Equal(requiresConversion, format.RequiresConversion);
    }

    [Theory]
    [InlineData("podcast.mp3", "audio/mpeg")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("bundle.zip", "application/zip")]
    [InlineData("legacy.doc", "application/msword")]
    [InlineData("noextension", "application/pdf")]
    [InlineData("", "application/pdf")]
    public void AnUnlistedUploadIsRefused(string fileName, string mediaType) =>
        Assert.False(SourceFormats.TryResolve(fileName, mediaType, out _));

    [Fact]
    public void AMediaTypeThatContradictsTheExtensionIsRefused()
    {
        // The pair is what the POST policy pins, so disagreeing halves cannot both be signed for.
        Assert.False(SourceFormats.TryResolve("report.pdf", "audio/mpeg", out _));
        Assert.False(SourceFormats.TryResolve("sheet.xlsx", "application/pdf", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("application/octet-stream")]
    public void ABrowserWithNoOpinionIsBelievedAboutNothingButTheExtension(string mediaType)
    {
        // Chrome reports nothing for .md and octet-stream for .msg. Refusing those would refuse
        // perfectly ordinary uploads, so the extension decides and the canonical type is used.
        Assert.True(SourceFormats.TryResolve("message.msg", mediaType, out var format));
        Assert.Equal("application/vnd.ms-outlook", format.MediaType);
    }

    [Fact]
    public void AMediaTypeWithParametersStillMatches() =>
        Assert.True(SourceFormats.TryResolve("notes.txt", "text/plain; charset=utf-8", out _));

    [Fact]
    public void WindowsReportingCsvAsAnExcelTypeIsAccepted() =>
        Assert.True(SourceFormats.TryResolve("rows.csv", "application/vnd.ms-excel", out _));

    [Fact]
    public void TheExtensionComesFromTheTableNotFromThePath()
    {
        Assert.True(SourceFormats.TryResolve("../../../etc/passwd.pdf", "application/pdf", out var format));

        // A key built from this can only ever be "<prefix>/<uploadId>.pdf".
        Assert.Equal(".pdf", format.Extension);
        Assert.DoesNotContain("/", format.Extension, StringComparison.Ordinal);
        Assert.DoesNotContain("..", format.Extension, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolutionIsCaseInsensitiveOnBothHalves()
    {
        Assert.True(SourceFormats.TryResolve("REPORT.PDF", "APPLICATION/PDF", out var format));
        Assert.Equal(".pdf", format.Extension);
    }

    [Fact]
    public void OnlyMarkdownAndPlainTextBypassTheConverter()
    {
        var passthrough = SourceFormats.All.Where(f => !f.RequiresConversion).Select(f => f.Extension).Order();
        Assert.Equal([".markdown", ".md", ".txt"], passthrough);
    }

    [Fact]
    public void EveryFormatThatProducesLayoutIsOneWeCanExtractGeometryFrom()
    {
        // Emitting a synthetic layout for a .docx would feed LayoutJoiner confident nonsense, so
        // the flag is true only for PDF and images (ingest plan §11).
        var layoutBearing = SourceFormats.All.Where(f => f.ProducesLayout).Select(f => f.Extension).Order();
        Assert.Equal([".bmp", ".jpeg", ".jpg", ".pdf", ".png", ".tif", ".tiff"], layoutBearing);
    }

    [Fact]
    public void NoTwoRowsClaimTheSameExtension() =>
        Assert.Equal(
            SourceFormats.All.Count,
            SourceFormats.All.Select(f => f.Extension).Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void TheAcceptAttributeOffersBothExtensionsAndMediaTypes()
    {
        var accept = SourceFormats.AcceptAttribute.Split(',');

        Assert.Contains(".pdf", accept);
        Assert.Contains("application/pdf", accept);
        Assert.DoesNotContain(".zip", accept);
    }

    [Fact]
    public void TheCapIsTenMebibytes() => Assert.Equal(10_485_760, SourceFormats.DefaultMaxBytes);
}
