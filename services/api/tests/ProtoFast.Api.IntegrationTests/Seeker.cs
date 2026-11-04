using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop.ModelRuns;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using DocumentKindRecord = ProtoFast.Data.OffTheLoop.Entities.DocumentKind;
using DocumentRecord = ProtoFast.Data.OffTheLoop.Entities.Document;
using Xunit;
using Loop = ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>A fresh OffTheLoop user; each call runs in its own scope, as a gRPC call would.</summary>
public sealed class Seeker(StoryDatabase database)
{
    public string UserId { get; } = $"seeker-{Guid.NewGuid():N}";

    public Task<TReply> Companies<TReply>(Func<CompanyService, ServerCallContext, Task<TReply>> call) => CallAs(call);

    public Task<TReply> Applications<TReply>(Func<ApplicationService, ServerCallContext, Task<TReply>> call) => CallAs(call);

    public Task<TReply> Notes<TReply>(Func<NoteService, ServerCallContext, Task<TReply>> call) => CallAs(call);

    public Task<TReply> Todos<TReply>(Func<TodoService, ServerCallContext, Task<TReply>> call) => CallAs(call);

    public Task<TReply> Goals<TReply>(Func<GoalService, ServerCallContext, Task<TReply>> call) => CallAs(call);

    public Task<TReply> Documents<TReply>(Func<LoopDocumentService, ServerCallContext, Task<TReply>> call) => CallAs(call);

    public Task<TReply> Runs<TReply>(Func<RunsService, ServerCallContext, Task<TReply>> call) => CallAs(call);

    public Task<TReply> Resumes<TReply>(Func<ResumeService, ServerCallContext, Task<TReply>> call) => CallAs(call);

    /// <summary>The model this user's runs research with.</summary>
    public void Script(IResearchModel model) => database.Services.GetRequiredService<ScriptedModels>().Use(UserId, model);

    /// <summary>The model this user's runs write with.</summary>
    public void Script(IWritingModel model) => database.Services.GetRequiredService<ScriptedModels>().Use(UserId, model);

    public async Task<Run> StartRunAsync(RunWork work, string label = "") =>
        (await Runs((s, c) => s.StartRun(new StartRunRequest { Label = label, Work = work }, c))).Run;

    public Task<GetRunReply> GetRunAsync(string runId) =>
        Runs((s, c) => s.GetRun(new GetRunRequest { RunId = runId }, c));

    public async Task<IReadOnlyList<Run>> ListRunsAsync() =>
        (await Runs((s, c) => s.ListRuns(new ListRunsRequest(), c))).Runs;

    /// <summary>What a watch streams until it returns; watch only runs that have ended, or it waits out its time.</summary>
    public async Task<List<WatchRunReply>> WatchRunAsync(WatchRunRequest request)
    {
        var replies = new RecordingStreamWriter<WatchRunReply>();
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(UserId);
        await scope.ServiceProvider.GetRequiredService<RunsService>().WatchRun(request, replies, new TestServerCallContext());
        return replies.Messages;
    }

    /// <summary>Claims this user's oldest queued run and runs it, as the worker would.</summary>
    public async Task<bool> RunNextAsync(CancellationToken stopping = default)
    {
        var run = await database.Services.GetRequiredService<RunStore>().ClaimAsync(TestContext.Current.CancellationToken, UserId);
        if (run is null)
        {
            return false;
        }

        await database.Services.GetRequiredService<RunExecutor>().RunAsync(run, stopping);
        return true;
    }

    public async Task Research(IResearchModel model, Func<ResearcherService, ServerCallContext, Task> call)
    {
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(UserId);
        var service = ActivatorUtilities.CreateInstance<ResearcherService>(
            scope.ServiceProvider, model, TimeProvider.System, NullLogger<ResearcherService>.Instance);
        await call(service, new TestServerCallContext());
    }

    public async Task<Company> CreateCompanyAsync(string name) =>
        (await Companies((s, c) => s.CreateCompany(new CreateCompanyRequest { Company = new Company { Name = name } }, c))).Company;

    public async Task<Company> UpdateCompanyAsync(Company company) =>
        (await Companies((s, c) => s.UpdateCompany(new UpdateCompanyRequest { Company = company }, c))).Company;

    public async Task<IReadOnlyList<Company>> ListCompaniesAsync() =>
        (await Companies((s, c) => s.ListCompanies(new ListCompaniesRequest(), c))).Companies;

    public async Task<Application> CreateApplicationAsync(Application application) =>
        (await Applications((s, c) => s.CreateApplication(new CreateApplicationRequest { Application = application }, c))).Application;

    public async Task<Application> UpdateApplicationAsync(Application application) =>
        (await Applications((s, c) => s.UpdateApplication(new UpdateApplicationRequest { Application = application }, c))).Application;

    public async Task<IReadOnlyList<Application>> ListApplicationsAsync() =>
        (await Applications((s, c) => s.ListApplications(new ListApplicationsRequest(), c))).Applications;

    public async Task<Note> CreateNoteAsync(Note note) =>
        (await Notes((s, c) => s.CreateNote(new CreateNoteRequest { Note = note }, c))).Note;

    public async Task<Note> UpdateNoteAsync(Note note) =>
        (await Notes((s, c) => s.UpdateNote(new UpdateNoteRequest { Note = note }, c))).Note;

    public async Task<IReadOnlyList<Note>> ListNotesAsync() =>
        (await Notes((s, c) => s.ListNotes(new ListNotesRequest(), c))).Notes;

    public async Task<IReadOnlyList<Note>> ImportStoriesAsync(params Note[] stories)
    {
        var request = new ImportStoriesRequest();
        request.Stories.AddRange(stories);
        return (await Notes((s, c) => s.ImportStories(request, c))).Stories;
    }

    public async Task<Resume> SaveResumeAsync(Resume resume) =>
        (await Resumes((s, c) => s.SaveResume(new SaveResumeRequest { Resume = resume }, c))).Resume;

    public async Task<IReadOnlyList<Resume>> ListResumesAsync() =>
        (await Resumes((s, c) => s.ListResumes(new ListResumesRequest(), c))).Resumes;

    public async Task<Todo> CreateTodoAsync(string label, string? dueOn = null)
    {
        var todo = new Todo { Label = label };
        if (dueOn is not null)
        {
            todo.DueOn = dueOn;
        }

        return (await Todos((s, c) => s.CreateTodo(new CreateTodoRequest { Todo = todo }, c))).Todo;
    }

    public async Task<IReadOnlyList<Todo>> ListTodosAsync() =>
        (await Todos((s, c) => s.ListTodos(new ListTodosRequest(), c))).Todos;

    public async Task<Goal> CreateGoalAsync(string kind, string target, string current = "") =>
        (await Goals((s, c) => s.CreateGoal(
            new CreateGoalRequest { Goal = new Goal { Kind = kind, Target = target, Current = current } }, c))).Goal;

    public async Task<IReadOnlyList<Goal>> ListGoalsAsync() =>
        (await Goals((s, c) => s.ListGoals(new ListGoalsRequest(), c))).Goals;

    /// <summary>
    /// What CreateDocumentUpload records, without its rate limit (which needs Redis); returns the
    /// pending document's id and the key its file goes to.
    /// </summary>
    public async Task<(string Id, string StorageKey)> StartUploadAsync(DocumentKindRecord kind, string fileName)
    {
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(UserId);
        var id = Guid.CreateVersion7();
        var document = new DocumentRecord
        {
            Id = id,
            Kind = kind,
            Name = DocumentMessages.NameFrom(fileName),
            FileName = fileName,
            MediaType = DocumentFormats.Of(fileName).MediaType,
            SizeBytes = 48_213,
            StorageKey = $"offtheloop/{UserId}/documents/{id}{Path.GetExtension(fileName)}",
        };
        using var unitOfWork = scope.ServiceProvider
            .GetRequiredService<IUnitOfWorkFactory<OffTheLoopDbContext>>()
            .CreateReadWrite(nameof(StartUploadAsync));
        await scope.ServiceProvider.GetRequiredService<IDocumentRepository>().AddAsync(document);
        await unitOfWork.CommitAsync();
        return (id.ToString(), document.StorageKey);
    }

    /// <summary>Starts an upload, lands its file and completes it.</summary>
    public async Task<Loop.Document> UploadAsync(DocumentKindRecord kind, string fileName, byte[]? content = null)
    {
        var (id, storageKey) = await StartUploadAsync(kind, fileName);
        database.Services.GetRequiredService<StoredFiles>().Put(storageKey, content);
        return (await Documents((s, c) => s.CompleteDocumentUpload(
            new Loop.CompleteDocumentUploadRequest { DocumentId = id }, c))).Document;
    }

    public async Task<IReadOnlyList<Loop.Document>> ListDocumentsAsync() =>
        (await Documents((s, c) => s.ListDocuments(new Loop.ListDocumentsRequest(), c))).Documents;

    public static Application NewApplication(string companyId, Stage stage = Stage.Applied, string appliedOn = "2026-10-14") =>
        new() { CompanyId = companyId, Stage = stage, AppliedOn = appliedOn };

    private async Task<TReply> CallAs<TService, TReply>(Func<TService, ServerCallContext, Task<TReply>> call)
        where TService : notnull
    {
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(UserId);
        return await call(scope.ServiceProvider.GetRequiredService<TService>(), new TestServerCallContext());
    }
}
