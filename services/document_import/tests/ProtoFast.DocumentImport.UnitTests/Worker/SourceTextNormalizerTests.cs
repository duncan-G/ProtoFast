using ProtoFast.DocumentImport.Worker.Import;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Worker;

public class SourceTextNormalizerTests
{
    [Fact]
    public void A_nul_between_words_becomes_the_space_it_stood_for()
    {
        Assert.Equal("뼈속부터 달라서 beating you", SourceTextNormalizer.Normalize("뼈속부터\0달라서\0beating you"));
    }

    [Fact]
    public void Line_breaks_become_newlines_and_other_control_characters_go()
    {
        Assert.Equal("INT. HOUSE\nRUMI\nHi.\n", SourceTextNormalizer.Normalize("INT. HOUSE\r\nRUMI\r\u0007Hi.\u0085\u007f\n"));
    }

    [Fact]
    public void Apostrophes_and_single_quotes_become_straight()
    {
        Assert.Equal("RUMI'S ROOM 'Hi,' she's", SourceTextNormalizer.Normalize("RUMI’S ROOM ‘Hi,’ sheʼs"));
    }

    [Fact]
    public void Tabs_page_breaks_and_double_quotes_are_kept()
    {
        const string text = "1.\f\tRUMI'S ROOM\n“Hi.”";

        Assert.Equal(text, SourceTextNormalizer.Normalize(text));
    }

    [Fact]
    public void Decomposed_hangul_and_accents_are_composed()
    {
        Assert.Equal("한 Café", SourceTextNormalizer.Normalize("한 Café"));
    }
}
