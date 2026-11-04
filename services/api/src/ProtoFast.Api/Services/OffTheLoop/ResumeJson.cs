using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// A resume as a model writes it, and back. What comes back is trimmed to fit, so it always saves:
/// over-long text is cut, and a month, link or entry that isn't one is left out.
/// </summary>
public static class ResumeJson
{
    private const string Fields = """
        {
          "name": { "type": "string", "description": "Their full name." },
          "headline": { "type": "string", "description": "One line under the name: their role and focus, like \"Senior backend engineer · payments\"." },
          "email": { "type": "string" },
          "phone": { "type": "string" },
          "location": { "type": "string", "description": "Where they are: \"Austin, TX\", \"London, UK\"." },
          "links": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "label": { "type": "string", "description": "\"GitHub\", \"LinkedIn\", \"portfolio\"." },
                "url": { "type": "string", "description": "The full address, starting https://." }
              },
              "required": ["label", "url"],
              "additionalProperties": false
            }
          },
          "summary": { "type": "string", "description": "A few sentences on who they are as an engineer." },
          "experience": {
            "type": "array",
            "description": "Jobs, most recent first.",
            "items": {
              "type": "object",
              "properties": {
                "company": { "type": "string" },
                "title": { "type": "string" },
                "location": { "type": "string" },
                "start": { "type": "string", "description": "YYYY-MM, or YYYY when only the year is known." },
                "end": { "type": "string", "description": "Like start; empty while it's current." },
                "summary": { "type": "string", "description": "One line on the team or what it did, if anything." },
                "highlights": { "type": "array", "items": { "type": "string" }, "description": "What they did and what came of it, one bullet each." },
                "stack": { "type": "array", "items": { "type": "string" }, "description": "Languages, frameworks and tools used, a word or two each." }
              },
              "required": ["company", "title", "location", "start", "end", "summary", "highlights", "stack"],
              "additionalProperties": false
            }
          },
          "projects": {
            "type": "array",
            "description": "Side projects, open source and notable pieces of work outside a job's own bullets.",
            "items": {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "link": { "type": "string", "description": "Its address, starting https://, or empty." },
                "start": { "type": "string", "description": "YYYY-MM, or YYYY when only the year is known." },
                "end": { "type": "string", "description": "Like start; empty while it's ongoing." },
                "summary": { "type": "string", "description": "One line on what it is." },
                "highlights": { "type": "array", "items": { "type": "string" } },
                "stack": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["name", "link", "start", "end", "summary", "highlights", "stack"],
              "additionalProperties": false
            }
          },
          "skills": {
            "type": "array",
            "description": "Skills in groups: languages, frameworks, data, infrastructure, tools, practices.",
            "items": {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "The group, lowercase." },
                "items": { "type": "array", "items": { "type": "string" }, "description": "A word or two each." }
              },
              "required": ["name", "items"],
              "additionalProperties": false
            }
          },
          "education": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "school": { "type": "string" },
                "degree": { "type": "string", "description": "Degree and field: \"BSc Computer Science\"." },
                "start": { "type": "string", "description": "YYYY-MM, or YYYY." },
                "end": { "type": "string", "description": "YYYY-MM, or YYYY." },
                "details": { "type": "array", "items": { "type": "string" }, "description": "Honours, thesis, relevant courses, one each." }
              },
              "required": ["school", "degree", "start", "end", "details"],
              "additionalProperties": false
            }
          },
          "extras": {
            "type": "array",
            "description": "Anything else, in named lists: certifications, awards, talks, writing, open source, languages spoken, volunteering.",
            "items": {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "The list, lowercase." },
                "items": { "type": "array", "items": { "type": "string" }, "description": "One entry each, with its year when known." }
              },
              "required": ["name", "items"],
              "additionalProperties": false
            }
          }
        }
        """;

    private static readonly string[] MonthFormats = ["MMM yyyy", "MMMM yyyy", "MMM. yyyy", "MM/yyyy", "M/yyyy", "yyyy-MM-dd", "yyyy/MM"];

    private static readonly string[] Current = ["present", "current", "now", "ongoing", "today"];

    /// <summary>The resume's fields after <paramref name="extra"/>, all required.</summary>
    public static JsonElement Schema(params (string Name, string Json)[] extra)
    {
        var properties = new JsonObject();
        foreach (var (name, json) in extra)
        {
            properties[name] = JsonNode.Parse(json);
        }

        foreach (var (name, node) in JsonNode.Parse(Fields)!.AsObject())
        {
            properties[name] = node!.DeepClone();
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(properties.Select(p => (JsonNode)JsonValue.Create(p.Key)).ToArray()),
            ["additionalProperties"] = false,
        };
        return JsonSerializer.SerializeToElement(schema);
    }

    public static Resume ToResume(JsonElement answer)
    {
        var resume = new Resume
        {
            Name = Text(answer, "name", OffTheLoopLimits.Name),
            Headline = Text(answer, "headline", OffTheLoopLimits.Line),
            Email = Text(answer, "email", OffTheLoopLimits.Name),
            Phone = Text(answer, "phone", OffTheLoopLimits.Label),
            Location = Text(answer, "location", OffTheLoopLimits.Label),
            Summary = Text(answer, "summary", OffTheLoopLimits.Text),
        };
        resume.Links.AddRange(Objects(answer, "links", OffTheLoopLimits.ListItems)
            .Select(link => new ResumeLink { Label = Text(link, "label", OffTheLoopLimits.Label), Url = Url(Text(link, "url", OffTheLoopLimits.Url)) })
            .Where(link => link.Url.Length > 0));
        resume.Experience.AddRange(Objects(answer, "experience", OffTheLoopLimits.ResumeEntries).Select(Role).Where(r => r.Company.Length > 0));
        resume.Projects.AddRange(Objects(answer, "projects", OffTheLoopLimits.ResumeEntries).Select(Project).Where(p => p.Name.Length > 0));
        resume.Skills.AddRange(Objects(answer, "skills", OffTheLoopLimits.ListItems).Select(g => Group(g, OffTheLoopLimits.Label)).Where(g => g.Name.Length > 0));
        resume.Education.AddRange(Objects(answer, "education", OffTheLoopLimits.ResumeEntries).Select(School).Where(s => s.School.Length > 0));
        resume.Extras.AddRange(Objects(answer, "extras", OffTheLoopLimits.ListItems).Select(g => Group(g, OffTheLoopLimits.Line)).Where(g => g.Name.Length > 0));
        return resume;
    }

    public static string Text(JsonElement value, string name, int max)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        var text = field.GetString()!.Trim();
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }

    public static IEnumerable<string> Items(JsonElement value, string name, int max) =>
        value.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!.Trim())
                .Where(item => item.Length > 0)
                .Select(item => item.Length <= max ? item : item[..(max - 1)].TrimEnd() + "…")
                .Distinct()
                .Take(OffTheLoopLimits.ListItems)
            : [];

    private static IEnumerable<JsonElement> Objects(JsonElement value, string name, int max) =>
        value.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Take(max)
            : [];

    private static ResumeRole Role(JsonElement value)
    {
        var role = new ResumeRole
        {
            Company = Text(value, "company", OffTheLoopLimits.Name),
            Title = Text(value, "title", OffTheLoopLimits.Name),
            Location = Text(value, "location", OffTheLoopLimits.Label),
            Start = Month(Text(value, "start", OffTheLoopLimits.Label)),
            End = Month(Text(value, "end", OffTheLoopLimits.Label)),
            Summary = Text(value, "summary", OffTheLoopLimits.Summary),
        };
        role.Highlights.AddRange(Items(value, "highlights", OffTheLoopLimits.Line));
        role.Stack.AddRange(Items(value, "stack", OffTheLoopLimits.Label));
        return role;
    }

    private static ResumeProject Project(JsonElement value)
    {
        var project = new ResumeProject
        {
            Name = Text(value, "name", OffTheLoopLimits.Name),
            Link = Url(Text(value, "link", OffTheLoopLimits.Url)),
            Start = Month(Text(value, "start", OffTheLoopLimits.Label)),
            End = Month(Text(value, "end", OffTheLoopLimits.Label)),
            Summary = Text(value, "summary", OffTheLoopLimits.Summary),
        };
        project.Highlights.AddRange(Items(value, "highlights", OffTheLoopLimits.Line));
        project.Stack.AddRange(Items(value, "stack", OffTheLoopLimits.Label));
        return project;
    }

    private static ResumeSchool School(JsonElement value)
    {
        var school = new ResumeSchool
        {
            School = Text(value, "school", OffTheLoopLimits.Name),
            Degree = Text(value, "degree", OffTheLoopLimits.Name),
            Start = Month(Text(value, "start", OffTheLoopLimits.Label)),
            End = Month(Text(value, "end", OffTheLoopLimits.Label)),
        };
        school.Details.AddRange(Items(value, "details", OffTheLoopLimits.Line));
        return school;
    }

    private static ResumeGroup Group(JsonElement value, int max)
    {
        var group = new ResumeGroup { Name = Text(value, "name", OffTheLoopLimits.Label) };
        group.Items.AddRange(Items(value, "items", max));
        return group;
    }

    /// <summary>Models write months many ways; "Present" is an open end.</summary>
    private static string Month(string text)
    {
        if (text.Length == 0 || Current.Contains(text.ToLowerInvariant()) || LoopText.IsMonth(text))
        {
            return LoopText.IsMonth(text) ? text : "";
        }

        return DateTime.TryParseExact(text, MonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            ? month.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : "";
    }

    private static string Url(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? text : "";
}
