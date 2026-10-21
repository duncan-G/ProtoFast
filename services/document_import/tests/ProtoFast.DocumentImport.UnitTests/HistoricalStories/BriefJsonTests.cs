using ProtoFast.DocumentImport.Worker.HistoricalStories;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.HistoricalStories;

public sealed class BriefJsonTests
{
    private const string Brief = """
        {
          "title": "The Ride", "moment": "A rider warns the countryside.", "when": "April 18, 1775",
          "where": "Middlesex County", "summary": "He rides.",
          "background": [{ "text": "British troops occupy Boston.", "sources": [14] },
                         { "text": "The colonists store powder at Concord.", "sources": [14] },
                         { "text": "Revere had ridden for the Committee before.", "sources": [11] }],
          "aftermath": [{ "text": "The war begins at Lexington.", "sources": [12] }],
          "people": [{ "name": "Paul Revere", "role": "rider", "sources": ["11"] }],
          "timeline": [{ "when": "10 pm", "what": "He crosses the river.", "sources": [11] },
                       { "when": "midnight", "what": "He reaches Lexington.", "sources": [12] }],
          "places": [], "details": [],
          "quotes": [{ "speaker": "Revere", "words": "The regulars are coming out.", "source": 13 }],
          "sources": [{ "passageId": 11, "why": "His own deposition." }]
        }
        """;

    [Fact]
    public void A_brief_whose_citations_were_all_read_is_ready()
    {
        var brief = BriefJson.Deserialize(Brief);
        Assert.Equal([11L, 12L, 13L, 14L], brief.CitedPassages().Order());
        Assert.Empty(BriefJson.Problems(brief, new HashSet<long> { 11, 12, 13, 14 }));
    }

    [Fact]
    public void A_brief_citing_unread_passages_is_sent_back_naming_them()
    {
        var problem = Assert.Single(BriefJson.Problems(BriefJson.Deserialize(Brief), new HashSet<long> { 11 }));
        Assert.Contains("12, 14, 13", problem);
    }

    [Fact]
    public void A_brief_without_people_or_enough_events_is_not_ready()
    {
        var thin = BriefJson.Deserialize(Brief) with { People = [], Timeline = [] };
        Assert.Equal(2, BriefJson.Problems(thin, new HashSet<long> { 11, 12, 13, 14 }).Count);
    }

    [Fact]
    public void A_brief_without_enough_background_for_the_narrator_is_not_ready()
    {
        var bare = BriefJson.Deserialize(Brief) with { Background = [new BriefFact("British troops occupy Boston.", [14])] };
        Assert.Contains("background", Assert.Single(BriefJson.Problems(bare, new HashSet<long> { 11, 12, 13, 14 })));
    }
}
