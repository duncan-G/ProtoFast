using ProtoFast.DocumentImport.History.Gutenberg;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.History;

public sealed class GutenbergTextTests
{
    [Fact]
    public void The_licence_wrapper_is_cut_off()
    {
        var text = """
            The Project Gutenberg eBook of A Diary
            *** START OF THE PROJECT GUTENBERG EBOOK A DIARY ***

            April 19. The regulars came at dawn.

            *** END OF THE PROJECT GUTENBERG EBOOK A DIARY ***
            Licence follows.
            """;
        Assert.Equal("April 19. The regulars came at dawn.", GutenbergText.StripBoilerplate(text));
    }

    [Fact]
    public void Paragraphs_pack_into_passages_without_splitting_one()
    {
        var paragraphs = Enumerable.Range(1, 6).Select(i => $"Paragraph {i} " + new string('x', 30)).ToList();
        var passages = GutenbergText.Passages(string.Join("\r\n\r\n", paragraphs), size: 100);

        Assert.All(passages, p => Assert.True(p.Length <= 100));
        Assert.Equal(string.Join("\n\n", paragraphs), string.Join("\n\n", passages));
    }

    [Fact]
    public void A_paragraph_longer_than_a_passage_splits_between_sentences()
    {
        var paragraph = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"Sentence number {i} ends here."));
        var passages = GutenbergText.Passages(paragraph, size: 80);

        Assert.True(passages.Count > 1);
        Assert.All(passages, p => Assert.EndsWith("ends here.", p));
        Assert.Equal(paragraph, string.Join(" ", passages));
    }

    [Fact]
    public void Line_breaks_inside_a_paragraph_and_nul_characters_become_spaces() =>
        Assert.Equal(["One line wrapped onto the next."], GutenbergText.Passages("One line\nwrapped onto\0the next."));
}
