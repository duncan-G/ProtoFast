using System.Security;
using System.Text.Json;
using Grpc.Core;
using ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class ResumeReading
{
    private const string System = """
        You read a resume for someone using OffTheLoop, a job-search companion. What you find is merged into their general resume, which holds everything that could go on a resume; they review it before it's saved.

        Take everything the resume says: every job, project, skill, school, certification, award, talk and link. Keep its wording: copy each bullet point as a highlight, as written, and never add, guess or polish. Leave a field or list empty when the resume doesn't give it.

        Write months as YYYY-MM, or YYYY when only the year is given, and leave a current job's end empty. Group the skills as the resume groups them, or by kind when it lists them in one run.
        """;

    private static readonly JsonElement Schema = ResumeJson.Schema((
        "outcome",
        """{ "type": "string", "enum": ["read", "not_a_resume", "unreadable"], "description": "read when you read a resume; otherwise why not, with every other field empty." }"""));

    public static ResearchBrief Brief(ResumeFile file, string fileName, DateOnly today)
    {
        var name = SecurityElement.Escape(fileName);
        var user = file.Attachment is null
            ? $"<resume file=\"{name}\">\n{file.Text}\n</resume>"
            : $"Their resume is the attached file, {name}.";
        return new ResearchBrief(System, $"{user}\n\nToday is {LoopText.Format(today)}.", Schema, Web: false, file.Attachment);
    }

    public static Resume ToResume(JsonElement answer) =>
        ResumeJson.Text(answer, "outcome", 32) switch
        {
            "not_a_resume" => throw Failed(LoopErrors.ResumeNotAResume),
            "unreadable" => throw Failed(LoopErrors.ResumeUnreadable),
            _ => ResumeJson.ToResume(answer),
        };

    private static RpcException Failed(string message) => new(new Status(StatusCode.FailedPrecondition, message));
}
