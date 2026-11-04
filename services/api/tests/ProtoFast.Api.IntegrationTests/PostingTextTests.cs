using System.Net;
using System.Text.Json;
using ProtoFast.Api.Services.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class PostingTextTests
{
    private const string Duties =
        "You'll design, build and run the services that move money between ledgers, own their on-call, and work "
        + "with product on what ships next. The team is six engineers across two timezones, and ships weekly.";

    [Fact]
    public void A_declared_job_posting_is_read_as_its_title_and_description()
    {
        var declared = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new object[]
            {
                new Dictionary<string, string> { ["@type"] = "Organization", ["description"] = "Not the posting." },
                new Dictionary<string, string>
                {
                    ["@type"] = "JobPosting",
                    ["title"] = "Senior SWE, Payments",
                    ["description"] = WebUtility.HtmlEncode(
                        $"<p>{Duties}</p><h2>What we want</h2><ul><li><p>5+ years backend</p></li><li>Go or Java</li></ul>"),
                },
            },
        });
        var html = $"""
            <html><head><script type="application/ld+json">{declared}</script></head>
            <body><nav>Jobs</nav><main><p>Apply now</p></main></body></html>
            """;

        Assert.Equal(
            $"Senior SWE, Payments\n\n{Duties}\n\nWhat we want\n\n• 5+ years backend\n• Go or Java",
            PostingText.Read(html));
    }

    [Fact]
    public void A_posting_marked_up_as_microdata_is_read_without_its_employers_description()
    {
        var html = $$"""
            <html><head><script type="application/ld+json">{"@type": "JobPosting", </script></head><body>
            <div class="jobDisplayShell" itemscope="itemscope" itemtype="http://schema.org/JobPosting">
              <span itemprop="hiringOrganization" itemscope itemtype="http://schema.org/Organization">
                <meta itemprop="description" content="A defense company.">
              </span>
              <h1><span itemprop="title">Software Engineer II
              </span></h1>
              <a href="/apply">Apply now</a>
              <span itemprop="description"><span class="jobdescription">
                <p><strong>Job ID: 115818 <br></strong></p>
                <p>{{Duties}}</p>
                <div><div><H2 style="font-size:1.0em"><b>Job Summary</b></H2></div>
                <div><p>Business Unit: EOIS<br>Location: Dallas, Tx<br>Type: Exempt&nbsp;Full-time</p></div></div>
              </span></span>
            </div></body></html>
            """;

        Assert.Equal(
            $"Software Engineer II\n\nJob ID: 115818\n\n{Duties}\n\nJob Summary\n\n"
            + "Business Unit: EOIS\nLocation: Dallas, Tx\nType: Exempt Full-time",
            PostingText.Read(html));
    }

    [Fact]
    public void Without_a_declared_posting_the_main_content_is_read_without_the_pages_furniture()
    {
        var html = $"""
            <html><body><header><a href="/">Northwind careers</a></header><nav>Home · Jobs</nav>
            <main>
              <h1>Staff Engineer</h1>
              <p>{Duties}</p>
              <form><label>Email</label><button>Apply</button></form>
              <script>track()</script>
              <div hidden>Cookie settings</div>
              <div aria-hidden="true">Share</div>
            </main>
            <footer>© Northwind</footer></body></html>
            """;

        Assert.Equal($"Staff Engineer\n\n{Duties}", PostingText.Read(html));
    }

    [Fact]
    public void A_plain_text_description_keeps_its_own_lines()
    {
        var declared = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["@type"] = "JobPosting",
            ["title"] = "Senior SWE",
            ["description"] = $"Senior SWE\n\n{Duties}\n- Go\n- Postgres &amp; Redis",
        });
        var html = $"""<script type="application/ld+json">{declared}</script>""";

        Assert.Equal($"Senior SWE\n\n{Duties}\n- Go\n- Postgres & Redis", PostingText.Read(html));
    }

    [Fact]
    public void A_page_that_loads_its_posting_with_script_shows_none()
    {
        Assert.Equal("", PostingText.Read("""<html><body><div id="root"></div><script src="/app.js"></script></body></html>"""));
        Assert.Equal("", PostingText.Read("""<html><body><main><p>Loading…</p></main></body></html>"""));
    }

    [Fact]
    public void A_long_posting_is_cut_to_what_an_application_keeps()
    {
        var html = $"<main><p>{string.Join(' ', Enumerable.Repeat(Duties, 200))}</p></main>";

        var posting = PostingText.Read(html);

        Assert.True(posting.Length <= OffTheLoopLimits.Posting);
        Assert.StartsWith(Duties, posting);
    }
}
