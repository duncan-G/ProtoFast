using ProtoFast.DocumentImport.History.Gutenberg;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.History;

public sealed class GutenbergCatalogTests
{
    private const string Catalog = """
        Text#,Type,Issued,Title,Language,Authors,Subjects,LoCC,Bookshelves
        1,Text,1971-12-01,The Declaration of Independence,en,"Jefferson, Thomas, 1743-1826","United States -- History -- Revolution, 1775-1783 -- Sources",E201; JK,"Category: History - American"
        2,Text,1972-12-01,"Pickett's Men
        A Fragment of War History",en,"Harrison, Walter","United States -- History -- Civil War, 1861-65",E581,
        3,Text,1990-01-01,The Red Badge of Courage,en,"Crane, Stephen","War stories",PS1449; E,"Category: History - American"
        4,Text,1990-01-01,Canadian Voyageurs,en,,"Canada -- History -- 19th century",F1030,
        5,Sound,1990-01-01,A Speech,en,,,E456,
        6,Text,1990-01-01,Une histoire,fr,,,E178,
        7,Text,1990-01-01,Life on the Plains,en,,,GV1,"Category: History - American"
        8,Text,1990-01-01,Ohio Valley,en,,,F516,
        """;

    [Fact]
    public void Only_english_american_history_texts_are_kept()
    {
        var books = GutenbergCatalog.AmericanHistory(new StringReader(Catalog)).ToList();
        Assert.Equal([1, 2, 7, 8], books.Select(b => b.Id));
    }

    [Fact]
    public void A_title_on_two_lines_joins_with_a_dash_and_subject_years_give_the_span()
    {
        var books = GutenbergCatalog.AmericanHistory(new StringReader(Catalog)).ToDictionary(b => b.Id);
        Assert.Equal("Pickett's Men — A Fragment of War History", books[2].Title);
        Assert.Equal("Harrison, Walter", books[2].Authors);
        Assert.Equal((1861, 1865), (books[2].FirstYear, books[2].LastYear));
        Assert.Equal((1775, 1783), (books[1].FirstYear, books[1].LastYear));
        Assert.Null(books[7].FirstYear);
    }
}
