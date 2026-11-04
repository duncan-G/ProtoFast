using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ApplicationRecord = ProtoFast.Data.OffTheLoop.Entities.Application;
using ApplicationSourceRecord = ProtoFast.Data.OffTheLoop.Entities.ApplicationSource;
using ApplicationStageRecord = ProtoFast.Data.OffTheLoop.Entities.ApplicationStage;
using ArrangementRecord = ProtoFast.Data.OffTheLoop.Entities.WorkArrangement;
using RoundRecord = ProtoFast.Data.OffTheLoop.Entities.Round;
using RoundStateRecord = ProtoFast.Data.OffTheLoop.Entities.RoundState;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class ApplicationMessages
{
    private const int MaxRounds = 12;

    public static Application ToMessage(ApplicationRecord application)
    {
        var message = new Application
        {
            Id = application.Id.ToString(),
            CompanyId = application.CompanyId.ToString(),
            Company = application.Company.Name,
            Role = application.Role,
            Stage = ToMessage(application.Stage),
            Closed = application.Closed,
            Source = ToMessage(application.Source),
            SourceDetail = application.SourceDetail,
            Resume = application.Resume,
            PostingUrl = application.PostingUrl,
            Posting = application.Posting,
            NextStep = application.NextStep,
            PostedPay = application.PostedPay,
            AskedPay = application.AskedPay,
            OfferedPay = application.OfferedPay,
            OfferTerms = application.OfferTerms,
            Team = application.Team,
            ReportsTo = application.ReportsTo,
            Arrangement = ToMessage(application.Arrangement),
            Location = application.Location,
            Hours = application.Hours,
            Learned = application.Learned,
            LastModifiedUnixSeconds = LoopText.UnixSeconds(application.DateLastModified),
            ResumeId = application.ResumeId?.ToString() ?? "",
            CvId = application.CvId?.ToString() ?? "",
        };
        if (application.AppliedOn is { } appliedOn)
        {
            message.AppliedOn = LoopText.Format(appliedOn);
        }

        if (application.NextOn is { } nextOn)
        {
            message.NextOn = LoopText.Format(nextOn);
        }

        if (application.SponsorsVisa is { } sponsorsVisa)
        {
            message.SponsorsVisa = sponsorsVisa;
        }

        message.Wants.AddRange(application.Wants);
        message.Questions.AddRange(application.Questions);
        message.Stack.AddRange(application.Stack);
        message.Benefits.AddRange(application.Benefits);
        message.Rounds.AddRange(application.Rounds.Select(r => new Round { Label = r.Label, State = ToMessage(r.State) }));
        return message;
    }

    /// <summary>The company is checked by the caller, which can see whose it is.</summary>
    public static ApplicationRecord ToRecord(Application message)
    {
        var rounds = message.Rounds
            .Select(r => new RoundRecord
            {
                Label = LoopText.Optional(r.Label, OffTheLoopLimits.Label, "A round’s name"),
                State = ToRecord(r.State),
            })
            .Where(r => r.Label.Length > 0)
            .ToList();
        if (rounds.Count > MaxRounds)
        {
            throw LoopErrors.Invalid($"A process can have at most {MaxRounds} rounds.");
        }

        return new ApplicationRecord
        {
            CompanyId = Guid.TryParse(message.CompanyId, out var companyId)
                ? companyId
                : throw LoopErrors.Invalid(LoopErrors.CompanyNotFound),
            Role = LoopText.Optional(message.Role, OffTheLoopLimits.Name, "The role"),
            Stage = ToRecord(message.Stage),
            Closed = message.Closed,
            AppliedOn = message.HasAppliedOn ? LoopText.OptionalDay(message.AppliedOn, "The day you applied") : null,
            Source = ToRecord(message.Source),
            SourceDetail = LoopText.Optional(message.SourceDetail, OffTheLoopLimits.Label, "Where it came from"),
            Resume = LoopText.Optional(message.Resume, OffTheLoopLimits.Label, "The resume version"),
            ResumeId = DocumentOf(message.ResumeId, LoopErrors.ResumeNotFound),
            CvId = DocumentOf(message.CvId, LoopErrors.CvNotFound),
            PostingUrl = LoopText.Url(message.PostingUrl, OffTheLoopLimits.Url, "The posting link"),
            Posting = LoopText.Optional(message.Posting, OffTheLoopLimits.Posting, "The saved posting"),
            NextStep = LoopText.Optional(message.NextStep, OffTheLoopLimits.Line, "The next step"),
            NextOn = message.HasNextOn ? LoopText.OptionalDay(message.NextOn, "The next step’s day") : null,
            PostedPay = LoopText.Optional(message.PostedPay, OffTheLoopLimits.Label, "The posted range"),
            AskedPay = LoopText.Optional(message.AskedPay, OffTheLoopLimits.Label, "What you asked"),
            OfferedPay = LoopText.Optional(message.OfferedPay, OffTheLoopLimits.Label, "The offer"),
            OfferTerms = LoopText.Optional(message.OfferTerms, OffTheLoopLimits.Line, "The offer terms"),
            Wants = LoopText.Items(message.Wants, OffTheLoopLimits.Line, "What they want"),
            Questions = LoopText.Items(message.Questions, OffTheLoopLimits.Line, "Questions to ask"),
            Team = LoopText.Optional(message.Team, OffTheLoopLimits.Line, "The team"),
            ReportsTo = LoopText.Optional(message.ReportsTo, OffTheLoopLimits.Name, "Who you’d report to"),
            Stack = LoopText.Items(message.Stack, OffTheLoopLimits.Label, "The stack"),
            Arrangement = ToRecord(message.Arrangement),
            Location = LoopText.Optional(message.Location, OffTheLoopLimits.Label, "The location"),
            Hours = LoopText.Optional(message.Hours, OffTheLoopLimits.Label, "The hours"),
            SponsorsVisa = message.HasSponsorsVisa ? message.SponsorsVisa : null,
            Benefits = LoopText.Items(message.Benefits, OffTheLoopLimits.Line, "Benefits"),
            Rounds = rounds,
            Learned = LoopText.Optional(message.Learned, OffTheLoopLimits.Text, "What you learned"),
        };
    }

    public static void Overwrite(ApplicationRecord target, ApplicationRecord source)
    {
        target.CompanyId = source.CompanyId;
        target.Role = source.Role;
        target.Stage = source.Stage;
        target.Closed = source.Closed;
        target.AppliedOn = source.AppliedOn;
        target.Source = source.Source;
        target.SourceDetail = source.SourceDetail;
        target.Resume = source.Resume;
        target.ResumeId = source.ResumeId;
        target.CvId = source.CvId;
        target.PostingUrl = source.PostingUrl;
        target.Posting = source.Posting;
        target.NextStep = source.NextStep;
        target.NextOn = source.NextOn;
        target.PostedPay = source.PostedPay;
        target.AskedPay = source.AskedPay;
        target.OfferedPay = source.OfferedPay;
        target.OfferTerms = source.OfferTerms;
        target.Wants = source.Wants;
        target.Questions = source.Questions;
        target.Team = source.Team;
        target.ReportsTo = source.ReportsTo;
        target.Stack = source.Stack;
        target.Arrangement = source.Arrangement;
        target.Location = source.Location;
        target.Hours = source.Hours;
        target.SponsorsVisa = source.SponsorsVisa;
        target.Benefits = source.Benefits;
        target.Rounds = source.Rounds;
        target.Learned = source.Learned;
    }

    private static Guid? DocumentOf(string id, string notFound) =>
        id.Length == 0 ? null : Guid.TryParse(id, out var parsed) ? parsed : throw LoopErrors.Invalid(notFound);

    private static Stage ToMessage(ApplicationStageRecord stage) => stage switch
    {
        ApplicationStageRecord.Applied => Stage.Applied,
        ApplicationStageRecord.Screen => Stage.Screen,
        ApplicationStageRecord.Tech => Stage.Tech,
        ApplicationStageRecord.Onsite => Stage.Onsite,
        ApplicationStageRecord.Offer => Stage.Offer,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    private static ApplicationStageRecord ToRecord(Stage stage) => stage switch
    {
        Stage.Applied => ApplicationStageRecord.Applied,
        Stage.Screen => ApplicationStageRecord.Screen,
        Stage.Tech => ApplicationStageRecord.Tech,
        Stage.Onsite => ApplicationStageRecord.Onsite,
        Stage.Offer => ApplicationStageRecord.Offer,
        _ => throw LoopErrors.Invalid("Pick the stage it’s at."),
    };

    private static Source ToMessage(ApplicationSourceRecord? source) => source switch
    {
        null => Source.Unspecified,
        ApplicationSourceRecord.Referral => Source.Referral,
        ApplicationSourceRecord.Recruiter => Source.Recruiter,
        ApplicationSourceRecord.JobBoard => Source.JobBoard,
        ApplicationSourceRecord.CareersPage => Source.CareersPage,
        ApplicationSourceRecord.Other => Source.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    private static ApplicationSourceRecord? ToRecord(Source source) => source switch
    {
        Source.Unspecified => null,
        Source.Referral => ApplicationSourceRecord.Referral,
        Source.Recruiter => ApplicationSourceRecord.Recruiter,
        Source.JobBoard => ApplicationSourceRecord.JobBoard,
        Source.CareersPage => ApplicationSourceRecord.CareersPage,
        Source.Other => ApplicationSourceRecord.Other,
        _ => throw LoopErrors.Invalid("Pick where it came from."),
    };

    private static Arrangement ToMessage(ArrangementRecord? arrangement) => arrangement switch
    {
        null => Arrangement.Unspecified,
        ArrangementRecord.Remote => Arrangement.Remote,
        ArrangementRecord.Hybrid => Arrangement.Hybrid,
        ArrangementRecord.Onsite => Arrangement.Onsite,
        _ => throw new ArgumentOutOfRangeException(nameof(arrangement), arrangement, null),
    };

    private static ArrangementRecord? ToRecord(Arrangement arrangement) => arrangement switch
    {
        Arrangement.Unspecified => null,
        Arrangement.Remote => ArrangementRecord.Remote,
        Arrangement.Hybrid => ArrangementRecord.Hybrid,
        Arrangement.Onsite => ArrangementRecord.Onsite,
        _ => throw LoopErrors.Invalid("The setup is remote, hybrid or onsite."),
    };

    private static RoundState ToMessage(RoundStateRecord state) => state switch
    {
        RoundStateRecord.Upcoming => RoundState.Upcoming,
        RoundStateRecord.Current => RoundState.Current,
        RoundStateRecord.Done => RoundState.Done,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static RoundStateRecord ToRecord(RoundState state) => state switch
    {
        RoundState.Upcoming or RoundState.Unspecified => RoundStateRecord.Upcoming,
        RoundState.Current => RoundStateRecord.Current,
        RoundState.Done => RoundStateRecord.Done,
        _ => throw LoopErrors.Invalid("A round is upcoming, current or done."),
    };
}
