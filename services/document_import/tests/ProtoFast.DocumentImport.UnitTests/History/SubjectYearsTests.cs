using ProtoFast.DocumentImport.History.Gutenberg;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.History;

public sealed class SubjectYearsTests
{
    [Theory]
    [InlineData("United States -- History -- Civil War, 1861-1865", 1861, 1865)]
    [InlineData("Massachusetts -- History -- 1775-83", 1775, 1783)]
    [InlineData("Boston (Mass.) -- History -- 1770; Lincoln, Abraham, 1809-1865", 1770, 1865)]
    [InlineData("United States -- Social life and customs -- 19th century", 1800, 1899)]
    [InlineData("Washington, George, 1732-", 1732, 1732)]
    public void Years_and_centuries_widen_the_span(string subjects, int first, int last) =>
        Assert.Equal((first, last), SubjectYears.Find(subjects));

    [Fact]
    public void Subjects_without_years_give_no_span() =>
        Assert.Equal((null, null), SubjectYears.Find("Indians of North America -- Wars", "Frontier and pioneer life"));
}
