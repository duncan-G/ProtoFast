using System.Security;
using System.Text;
using System.Text.Json;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using CompanyRecord = ProtoFast.Data.OffTheLoop.Entities.Company;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class CompanyResearch
{
    private const string System = """
        You research a company for someone deciding whether to work there. They keep what you find in OffTheLoop, their job-search tracker, beside their applications to it, and review it before saving.

        Search the web for the company and read the pages that answer best: its own site, product and pricing pages, careers and leadership pages, funding and financial news or filings, its engineering blog, and employee reviews. Prefer recent, primary sources; when sources disagree, take the most recent credible one. If the name is ambiguous, the about line and what's already known tell you which company it is.

        Answer with what you found. Write each value short, the way someone jots it in a tracker, not as a sentence; what it does and how it makes money are the exception, a sentence or two of plain words someone outside its industry would follow, not its marketing. A figure comes first and stands alone; context it needs (the year, which part of the business, the parent company's figure) goes in brackets after it, in a few words. Leave a field or list empty when you didn't find it: never guess or pad. What's already known stays unless you give a field a new value, so give a field only when you found it, and in a list only items that aren't already known in other words; an executive already among the known people isn't new.
        """;

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "about": { "type": "string", "description": "One lowercase line, its parts split by \" · \": industry, product, how they work. Like \"fintech · payments api · remote (us)\"." },
            "location": { "type": "string", "description": "Where it's headquartered, short: \"Burbank, CA\", \"London, UK\"; \"remote\" when it has no office." },
            "what_it_does": { "type": "string", "description": "What it actually does, in plain words: what it makes or sells, to whom, and what they use it for. One or two short sentences, no slogans: \"Makes software hospitals use to schedule nurses' shifts.\"" },
            "how_it_makes_money": { "type": "string", "description": "How it makes money: who pays it, for what, and how it's priced, the biggest source first with its share when known. One or two short sentences: \"Hospitals pay a yearly subscription per nurse; setup is billed once (~10% of revenue).\" When it earns nothing yet, what it runs on: \"No revenue yet; runs on venture funding.\"" },
            "revenue": { "type": "string", "description": "Annual revenue as a short figure, any context in brackets after: \"$140M\", \"~$50M ARR\", \"$8.4B (FY2025)\"." },
            "growth": { "type": "string", "description": "Revenue growth as a short figure, any context in brackets after: a stated target like \"2× by ’27\", or the recent rate like \"+40% YoY (FY2025)\". Not retention." },
            "headcount": { "type": "string", "description": "People employed as a short figure, any context in brackets after: \"~400\", \"~2,400 (parent ~231k)\"." },
            "funding": { "type": "string", "description": "Stage and total raised, short: \"Series C · $210M\", \"public\", \"bootstrapped\"." },
            "last_raised": { "type": "string", "description": "Month and year its last round closed: \"Mar 2025\". Empty when public." },
            "values": { "type": "array", "items": { "type": "string" }, "description": "Its stated values, a few words each, as it words them." },
            "practices": { "type": "array", "items": { "type": "string" }, "description": "How it works, a few words each: \"remote-first\", \"on-call 1 week in 6\", \"paid 3-day work trial\", \"no PMs\"." },
            "competitors": { "type": "array", "items": { "type": "string" }, "description": "Companies it competes with, names only." },
            "concerns": { "type": "array", "items": { "type": "string" }, "description": "What a candidate should weigh, a few words each with when: \"layoffs (Mar 2025)\", \"CEO left (2024)\", \"reviews say on-call is heavy\"." },
            "executives": {
              "type": "array",
              "description": "Who runs it now, at most eight: the CEO, founders still there, the rest of the C-suite, and whoever leads engineering, product and design. Their title as the company gives it, short: \"CEO · co-founder\", \"VP Engineering\".",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "role": { "type": "string" }
                },
                "required": ["name", "role"],
                "additionalProperties": false
              }
            }
          },
          "required": ["about", "location", "what_it_does", "how_it_makes_money", "revenue", "growth", "headcount", "funding", "last_raised", "values", "practices", "competitors", "concerns", "executives"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public static ResearchBrief Brief(CompanyRecord company, DateOnly today)
    {
        var known = new StringBuilder();
        Known(known, "about", company.About);
        Known(known, "location", company.Location);
        Known(known, "what_it_does", company.WhatItDoes);
        Known(known, "how_it_makes_money", company.HowItMakesMoney);
        Known(known, "revenue", company.Revenue);
        Known(known, "growth", company.Growth);
        Known(known, "headcount", company.Headcount);
        Known(known, "funding", company.Funding);
        Known(known, "last_raised", company.LastRaised);
        Known(known, "values", company.Values);
        Known(known, "practices", company.Practices);
        Known(known, "competitors", company.Competitors);
        Known(known, "concerns", company.Concerns);
        Known(known, "people", company.Contacts.Select(c => c.Role.Length > 0 ? $"{c.Name} ({c.Role})" : c.Name).ToList());

        var user = $"""
            <company>{SecurityElement.Escape(company.Name)}</company>

            <known>
            {(known.Length > 0 ? known.ToString().TrimEnd() : "nothing yet")}
            </known>

            Today is {LoopText.Format(today)}.
            """;
        return new ResearchBrief(System, user, Schema, Web: true);
    }

    public static CompanyFindings ToFindings(JsonElement input, IEnumerable<WebSource> sources)
    {
        var company = new Company
        {
            About = FindingsJson.Text(input, "about", OffTheLoopLimits.Line),
            Location = FindingsJson.Text(input, "location", OffTheLoopLimits.Label),
            WhatItDoes = FindingsJson.Text(input, "what_it_does", OffTheLoopLimits.Summary),
            HowItMakesMoney = FindingsJson.Text(input, "how_it_makes_money", OffTheLoopLimits.Summary),
            Revenue = FindingsJson.Text(input, "revenue", OffTheLoopLimits.Label),
            Growth = FindingsJson.Text(input, "growth", OffTheLoopLimits.Label),
            Headcount = FindingsJson.Text(input, "headcount", OffTheLoopLimits.Label),
            Funding = FindingsJson.Text(input, "funding", OffTheLoopLimits.Label),
            LastRaised = FindingsJson.Text(input, "last_raised", OffTheLoopLimits.Label),
        };
        company.Values.AddRange(FindingsJson.Items(input, "values", OffTheLoopLimits.Line));
        company.Practices.AddRange(FindingsJson.Items(input, "practices", OffTheLoopLimits.Line));
        company.Competitors.AddRange(FindingsJson.Items(input, "competitors", OffTheLoopLimits.Name));
        company.Concerns.AddRange(FindingsJson.Items(input, "concerns", OffTheLoopLimits.Line));
        company.Contacts.AddRange(Executives(input));

        var findings = new CompanyFindings { Company = company };
        findings.Sources.AddRange(sources);
        return findings;
    }

    /// <summary>Nameless and repeated people are dropped.</summary>
    private static IEnumerable<Contact> Executives(JsonElement input) =>
        input.TryGetProperty("executives", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new Contact
                {
                    Name = FindingsJson.Text(item, "name", OffTheLoopLimits.Name),
                    Role = FindingsJson.Text(item, "role", OffTheLoopLimits.Label),
                })
                .Where(contact => contact.Name.Length > 0)
                .DistinctBy(contact => contact.Name, StringComparer.OrdinalIgnoreCase)
                .Take(OffTheLoopLimits.ListItems)
            : [];

    private static void Known(StringBuilder known, string name, string value)
    {
        if (value.Length > 0)
        {
            known.AppendLine($"{name}: {value}");
        }
    }

    private static void Known(StringBuilder known, string name, List<string> items)
    {
        if (items.Count > 0)
        {
            known.AppendLine($"{name}: {string.Join("; ", items)}");
        }
    }
}
