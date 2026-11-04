using System.Security;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ApplicationRecord = ProtoFast.Data.OffTheLoop.Entities.Application;
using NoteRecord = ProtoFast.Data.OffTheLoop.Entities.Note;
using ResumeRecord = ProtoFast.Data.OffTheLoop.Entities.Resume;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class ResumeTailoring
{
    private const int MaxStories = 40;

    private const int MaxStory = 4000;

    private const string System = """
        You tailor a resume to one job for someone using OffTheLoop, a job-search companion. They keep a general resume there: everything that could go on a resume, drawn from their jobs and their git history. They also keep interview stories from real work. You write the resume they'll send this employer; they review it before saving.

        - Use only what the general resume and the stories say. Never invent an employer, title, date, number, skill or outcome, and don't inflate what they say: no "critical", "proven track record" or impact the sources don't claim. You may reword, merge, reorder and leave out.
        - Lead with what this job asks for: the jobs and projects that match the posting, and in each, the matching highlights first. Leave out what doesn't help. Keep it to one page for under ten years of experience, two at most otherwise: three to six highlights for recent jobs, fewer for older ones, and only the projects that earn their place.
        - Each highlight starts with a strong verb, past tense (present for a current job), and says what changed, with the numbers the sources give. Where a skill the posting names is one they have, use the posting's word for it.
        - A story can give a highlight or a number to the job it happened in; write it as a result, not as a story.
        - The summary is two or three sentences aimed at this role. The headline names the role they're going for, in their terms.
        - Skills: only those the general resume or a story names, never ones their links or tools merely imply; the posting's matches first, in a few groups.
        - Keep every job's company, title and dates as they are, and keep education.
        - The notes are for them, not the resume: two to six short lines on what you led with and why, and on what the posting asks for that their sources don't show.
        """;

    private static readonly JsonElement Schema = ResumeJson.Schema((
        "notes",
        """{ "type": "array", "items": { "type": "string" }, "description": "Two to six short lines for them: what you led with and why, and what the posting asks for that their sources don't show." }"""));

    /// <param name="application">With its company.</param>
    public static ResearchBrief Brief(
        ResumeRecord general, IReadOnlyList<NoteRecord> stories, ApplicationRecord application, string instructions, DateOnly today)
    {
        var parts = new List<string> { Job(application), $"<general_resume>\n{GeneralJson(general)}\n</general_resume>" };
        if (stories.Count > 0)
        {
            var told = new StringBuilder("<stories>\n");
            foreach (var story in stories.Take(MaxStories))
            {
                var body = story.Body.Length <= MaxStory ? story.Body : story.Body[..MaxStory];
                told.Append($"<story title=\"{SecurityElement.Escape(story.Title)}\" quality=\"{SecurityElement.Escape(story.Tag)}\">\n{body}\n</story>\n");
            }

            parts.Add(told.Append("</stories>").ToString());
        }

        if (instructions.Length > 0)
        {
            parts.Add($"<instructions>\n{instructions}\n</instructions>");
        }

        parts.Add($"Today is {LoopText.Format(today)}.");
        return new ResearchBrief(System, string.Join("\n\n", parts), Schema, Web: false);
    }

    /// <summary>Their name and how to reach them are facts, kept from the general resume.</summary>
    public static Resume ToResume(JsonElement answer, ResumeRecord general)
    {
        var resume = ResumeJson.ToResume(answer);
        resume.Name = general.Name;
        resume.Email = general.Email;
        resume.Phone = general.Phone;
        resume.Location = general.Location;
        resume.Links.Clear();
        resume.Links.AddRange(general.Links.Select(l => new ResumeLink { Label = l.Label, Url = l.Url }));
        resume.Notes.AddRange(ResumeJson.Items(answer, "notes", OffTheLoopLimits.Line));
        return resume;
    }

    private static string Job(ApplicationRecord application)
    {
        var company = application.Company!;
        var job = new StringBuilder("<job>\n");
        Line(job, "company", company.Name);
        Line(job, "about", company.About);
        Line(job, "what it does", company.WhatItDoes);
        Line(job, "values", string.Join("; ", company.Values));
        Line(job, "how it works", string.Join("; ", company.Practices));
        Line(job, "role", application.Role);
        Line(job, "team", application.Team);
        Line(job, "reports to", application.ReportsTo);
        Line(job, "stack", string.Join(", ", application.Stack));
        Line(job, "they want", string.Join("; ", application.Wants));
        if (application.Posting.Length > 0)
        {
            job.Append($"<posting>\n{application.Posting}\n</posting>\n");
        }

        return job.Append("</job>").ToString();
    }

    private static void Line(StringBuilder text, string name, string value)
    {
        if (value.Length > 0)
        {
            text.Append($"{name}: {value}\n");
        }
    }

    /// <summary>Without the commits behind each entry, which tailoring has no use for.</summary>
    private static string GeneralJson(ResumeRecord general)
    {
        var message = ResumeMessages.ToMessage(general);
        message.Id = "";
        message.LastModifiedUnixSeconds = 0;
        message.Notes.Clear();
        foreach (var role in message.Experience)
        {
            role.Evidence.Clear();
        }

        foreach (var project in message.Projects)
        {
            project.Evidence.Clear();
        }

        return JsonFormatter.Default.Format(message);
    }
}
