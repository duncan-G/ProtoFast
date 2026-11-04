using Grpc.Core;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>The messages reach users verbatim, so they are worded for them.</summary>
public static class LoopErrors
{
    public const string CompanyNotFound = "That company could not be found.";
    public const string ApplicationNotFound = "That application could not be found.";
    public const string NoteNotFound = "That note could not be found.";
    public const string TodoNotFound = "That to-do could not be found.";
    public const string GoalNotFound = "That goal could not be found.";
    public const string DocumentNotFound = "That file could not be found.";
    public const string DocumentNotArrived = "The file hasn’t reached storage. Upload it again.";
    public const string ResumeNotFound = "That resume could not be found.";
    public const string CvNotFound = "That CV could not be found.";
    public const string WriterOff = "Writing with AI isn’t set up here.";
    public const string WriterFailed = "The AI couldn’t finish. Try again in a moment.";
    public const string WriterRefused = "The AI wouldn’t write that. Try asking another way.";
    public const string ResearchOff = "Researching with AI isn’t set up here.";
    public const string ResearchFailed = "The research couldn’t finish. Try again in a moment.";
    public const string ResearchRefused = "The AI wouldn’t research that.";
    public const string PostingMissing = "Paste a link to the posting, or its text.";
    public const string PostingSignIn = "That page needs a sign-in to read. Paste the posting’s text instead.";
    public const string PostingGone = "That posting couldn’t be found; it may have come down. Paste its text if you kept it.";
    public const string PostingNotAPosting = "That page doesn’t look like a job posting.";
    public const string PostingUnreadable = "That page couldn’t be read. Paste the posting’s text instead.";
    public const string RunNotFound = "That AI work could not be found.";
    public const string RunEmpty = "There’s nothing for the AI to do.";
    public const string RunNoteMissing = "Say which note to write in.";
    public const string RunGaveUp = "This kept stopping part-way through. Try again.";
    public const string RunUnexpected = "Something went wrong part-way through. Try again.";
    public const string WriterStopped = "The AI stopped part-way through. Keep what it wrote, or try again.";

    public static RpcException NotFound(string message) => new(new Status(StatusCode.NotFound, message));

    public static RpcException Invalid(string message) => new(new Status(StatusCode.InvalidArgument, message));
}
