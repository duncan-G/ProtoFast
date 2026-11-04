using System.Security;
using System.Text.Json;
using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using CompanyRecord = ProtoFast.Data.OffTheLoop.Entities.Company;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class PostingResearch
{
    private const string System = """
        You read a job posting for someone tracking their job search in OffTheLoop. What you find fills in their application to that job and what they know of the employer; they review it before saving.

        Read the posting at the link. If the page comes back empty, as a shell that loads the job with script, or as a sign-in wall, search for the same posting elsewhere (the employer's careers page or a job board) and read it there. Take only what the posting says: leave a field or list empty when it doesn't say, never guess.

        Answer with what it says. Write each value short, the way someone jots it in a tracker, not as a sentence. The summary is the exception: a few lines in your own words.
        """;

    private const string PastedSystem = """
        You read a job posting for someone tracking their job search in OffTheLoop. What you find fills in their application to that job and what they know of the employer; they review it before saving.

        The posting's text is below, pasted by them. Take only what it says: leave a field or list empty when it doesn't say, never guess.

        Answer with what it says. Write each value short, the way someone jots it in a tracker, not as a sentence. Leave the page and summary fields empty: their pasted text is kept as it is.
        """;

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "outcome": { "type": "string", "enum": ["read", "sign_in", "gone", "not_a_posting", "unreadable"], "description": "read when you read a job posting; otherwise why not, with every other field empty." },
            "employer": { "type": "string", "description": "The company the posting is for, as it names itself." },
            "role": { "type": "string", "description": "The job title, as posted." },
            "page": { "type": "string", "description": "The link of the page you read the posting on, as you read it." },
            "summary": { "type": "string", "description": "The posting in your own words, kept in case its page can't be: a few short lines on the role, the team and company, what you'd do, what they want, pay and benefits. Never copy its sentences." },
            "posted_pay": { "type": "string", "description": "The posted pay range, written short in thousands without cents: \"$158,100.00 to $239,700.00 per year\" is \"$158–240k\"; \"£70–85k + equity\"; hourly as \"$60–75/h\"." },
            "team": { "type": "string", "description": "The team and what it works on: \"Payments Core, ledger + retries\"." },
            "reports_to": { "type": "string", "description": "Who the role reports to: \"Engineering Manager, Payments\"." },
            "arrangement": { "type": "string", "enum": ["remote", "hybrid", "onsite", "unknown"] },
            "location": { "type": "string", "description": "Where: \"SF, 3 days\", \"US only\", \"London or remote (UK)\"." },
            "hours": { "type": "string", "description": "The timezone or overlap it expects: \"4h overlap with ET\"." },
            "sponsors_visa": { "type": "string", "enum": ["yes", "no", "unknown"] },
            "stack": { "type": "array", "items": { "type": "string" }, "description": "Languages, frameworks, databases and tools it names, a word or two each." },
            "wants": { "type": "array", "items": { "type": "string" }, "description": "What they want in a candidate, one requirement each: \"5+ years backend\", \"Go or Java\"." },
            "benefits": { "type": "array", "items": { "type": "string" }, "description": "The benefits it lists, one each." },
            "questions": { "type": "array", "items": { "type": "string" }, "description": "Up to four things the posting leaves open that are worth asking them, each a short question." },
            "about": { "type": "string", "description": "One lowercase line on the employer, its parts split by \" · \": industry, product, how they work. Like \"fintech · payments api · remote (us)\"." },
            "values": { "type": "array", "items": { "type": "string" }, "description": "The employer's values, as the posting words them." },
            "practices": { "type": "array", "items": { "type": "string" }, "description": "How the employer works, one fact each: remote policy, on-call, interview process." }
          },
          "required": ["outcome", "employer", "role", "page", "summary", "posted_pay", "team", "reports_to", "arrangement", "location", "hours", "sponsors_visa", "stack", "wants", "benefits", "questions", "about", "values", "practices"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    /// <summary>Pasted text is read as it is; a link alone is fetched.</summary>
    public static ResearchBrief Brief(string url, string text, CompanyRecord? company, DateOnly today)
    {
        var parts = new List<string>();
        if (company is not null)
        {
            parts.Add($"<company>{SecurityElement.Escape(company.Name)}</company>");
        }

        if (text.Length > 0)
        {
            parts.Add($"<posting>\n{text}\n</posting>");
        }
        else
        {
            parts.Add($"<link>{url}</link>");
        }

        parts.Add($"Today is {LoopText.Format(today)}.");
        var user = string.Join("\n\n", parts);
        return text.Length > 0
            ? new ResearchBrief(PastedSystem, user, Schema, Web: false)
            : new ResearchBrief(System, user, Schema, Web: true);
    }

    /// <summary>Where to read the posting's text: the link, then the page the model read it on; none when it read none.</summary>
    public static IReadOnlyList<string> Pages(JsonElement input, string url)
    {
        if (FindingsJson.Text(input, "outcome", OffTheLoopLimits.Label) != "read")
        {
            return [];
        }

        var page = FindingsJson.Text(input, "page", OffTheLoopLimits.Url);
        return page != url && Uri.TryCreate(page, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? [url, page]
            : [url];
    }

    /// <summary>
    /// The link leads the sources, under its site's name. The posting is the pasted or fetched text, or
    /// else the model's summary.
    /// </summary>
    public static PostingFindings ToFindings(JsonElement input, string url, string posting, IEnumerable<WebSource> sources)
    {
        var outcome = FindingsJson.Text(input, "outcome", OffTheLoopLimits.Label);
        var problem = outcome switch
        {
            "read" => null,
            "sign_in" => LoopErrors.PostingSignIn,
            "gone" => LoopErrors.PostingGone,
            "not_a_posting" => LoopErrors.PostingNotAPosting,
            _ => LoopErrors.PostingUnreadable,
        };
        if (problem is not null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, problem));
        }

        var application = new Application
        {
            Role = FindingsJson.Text(input, "role", OffTheLoopLimits.Name),
            PostingUrl = url,
            Posting = posting.Length > 0 ? posting : FindingsJson.Text(input, "summary", OffTheLoopLimits.Posting),
            PostedPay = FindingsJson.Text(input, "posted_pay", OffTheLoopLimits.Label),
            Team = FindingsJson.Text(input, "team", OffTheLoopLimits.Line),
            ReportsTo = FindingsJson.Text(input, "reports_to", OffTheLoopLimits.Name),
            Arrangement = FindingsJson.Text(input, "arrangement", OffTheLoopLimits.Label) switch
            {
                "remote" => Arrangement.Remote,
                "hybrid" => Arrangement.Hybrid,
                "onsite" => Arrangement.Onsite,
                _ => Arrangement.Unspecified,
            },
            Location = FindingsJson.Text(input, "location", OffTheLoopLimits.Label),
            Hours = FindingsJson.Text(input, "hours", OffTheLoopLimits.Label),
        };
        switch (FindingsJson.Text(input, "sponsors_visa", OffTheLoopLimits.Label))
        {
            case "yes":
                application.SponsorsVisa = true;
                break;
            case "no":
                application.SponsorsVisa = false;
                break;
        }

        application.Stack.AddRange(FindingsJson.Items(input, "stack", OffTheLoopLimits.Label));
        application.Wants.AddRange(FindingsJson.Items(input, "wants", OffTheLoopLimits.Line));
        application.Benefits.AddRange(FindingsJson.Items(input, "benefits", OffTheLoopLimits.Line));
        application.Questions.AddRange(FindingsJson.Items(input, "questions", OffTheLoopLimits.Line));

        var company = new Company { About = FindingsJson.Text(input, "about", OffTheLoopLimits.Line) };
        company.Values.AddRange(FindingsJson.Items(input, "values", OffTheLoopLimits.Line));
        company.Practices.AddRange(FindingsJson.Items(input, "practices", OffTheLoopLimits.Line));

        var findings = new PostingFindings
        {
            Application = application,
            Company = company,
            Employer = FindingsJson.Text(input, "employer", OffTheLoopLimits.Name),
        };
        if (url.Length > 0)
        {
            findings.Sources.Add(new WebSource { Title = LoopText.Site(new Uri(url)), Url = url });
        }

        findings.Sources.AddRange(sources.Where(source => source.Url != url));
        return findings;
    }
}
