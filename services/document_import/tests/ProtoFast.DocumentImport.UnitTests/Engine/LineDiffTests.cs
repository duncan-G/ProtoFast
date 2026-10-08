using ProtoFast.DocumentImport.Engine.Briefing;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class LineDiffTests
{
    [Fact]
    public void Counts_and_shows_changed_lines_with_context()
    {
        const string before = "a\nb\nc\nd\ne\nf\ng\nh\ni\nj";
        const string after = "a\nb\nc\nD\ne\nf\ng\nh\ni\nj\nk";

        Assert.Equal((2, 1), LineDiff.Count(before, after));
        Assert.Equal("+2/−1 lines", LineDiff.Describe(before, after));
        Assert.Equal("  b\n  c\n- d\n+ D\n  e\n  f\n…\n  i\n  j\n+ k\n", LineDiff.Unified(before, after).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Identical_text_is_unchanged_and_new_text_is_all_added()
    {
        Assert.Equal("unchanged", LineDiff.Describe("x\r\ny\n", "x\ny"));
        Assert.Equal("", LineDiff.Unified("x\ny", "x\ny"));
        Assert.Equal("+ x\n+ y\n", LineDiff.Unified("", "x\ny").ReplaceLineEndings("\n"));
    }
}
