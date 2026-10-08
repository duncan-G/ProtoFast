using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Services;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Storage;
using Xunit;
using DocumentRecord = ProtoFast.Data.ThePlot.Entities.Document;
using DocumentUploadRecord = ProtoFast.Data.ThePlot.Entities.DocumentUpload;

namespace ProtoFast.Api.IntegrationTests;

public class DocumentProgressTests(StoryDatabase database)
{
    private readonly string _owner = $"writer-{Guid.NewGuid():N}";
    private readonly string _stranger = $"writer-{Guid.NewGuid():N}";

    private IRunLedger Ledger => database.Services.GetRequiredService<IRunLedger>();

    [Fact]
    public async Task Progress_comes_from_the_ledger_for_the_callers_uploads_only()
    {
        var queued = await UploadAsync(_owner, onDesk: true);
        var analysing = await UploadAsync(_owner, onDesk: true);
        var tagging = await UploadAsync(_owner, onDesk: true);
        var done = await UploadAsync(_owner, onDesk: false);
        var abandoned = await UploadAsync(_owner, onDesk: false);
        var theirs = await UploadAsync(_stranger, onDesk: true);
        var storyId = Guid.NewGuid().ToString();
        await Ledger.ReportAsync(analysing, new RunProgress(RunPhase.Running, "run", "scenes", Cost: 0.4125m), default);
        await Ledger.ReportAsync(tagging, new RunProgress(RunPhase.Finishing, "run", "mentions"), default);
        await Ledger.ReportAsync(done, new RunProgress(RunPhase.Finished, "run", ResultId: storyId), default);
        await Ledger.ReportAsync(theirs, new RunProgress(RunPhase.Running, "run", "library"), default);

        var reply = await CallAsync(_owner, (s, c) => s.GetImportProgress(
            new GetImportProgressRequest { UploadIds = { queued, analysing, tagging, done, abandoned, theirs } }, c));

        var imports = reply.Imports.ToDictionary(i => i.UploadId);
        Assert.Equal(new[] { queued, analysing, tagging, done }.Order(), imports.Keys.Order());
        Assert.Equal(ImportState.Queued, imports[queued].State);
        Assert.Equal((ImportState.Analysing, "scenes"), (imports[analysing].State, imports[analysing].Stage));
        Assert.Equal((ImportState.Saving, "mentions"), (imports[tagging].State, imports[tagging].Stage));
        Assert.Equal(412_500, imports[analysing].CostUsdMicros);
        Assert.Equal((ImportState.Done, storyId), (imports[done].State, imports[done].StoryId));
    }

    [Fact]
    public async Task Listed_documents_carry_their_import_progress()
    {
        var failed = await UploadAsync(_owner, onDesk: true);
        await Ledger.ReportAsync(failed, new RunProgress(RunPhase.Failed, Message: "unreadable"), default);

        var reply = await CallAsync(_owner, (s, c) => s.ListDocuments(new ListDocumentsRequest(), c));

        var document = Assert.Single(reply.Documents);
        Assert.Equal((ImportState.Failed, "unreadable"), (document.Import.State, document.Import.Message));
    }

    [Fact]
    public async Task Cancelling_takes_the_document_off_the_desk_and_holds_against_the_worker()
    {
        var running = await UploadAsync(_owner, onDesk: true);
        await Ledger.ReportAsync(running, new RunProgress(RunPhase.Running, "run", "scenes", Cost: 0.25m), default);

        var reply = await CallAsync(_owner, (s, c) => s.CancelImport(new CancelImportRequest { UploadId = running }, c));
        await Ledger.ReportAsync(running, new RunProgress(RunPhase.Retrying, Message: "again"), default);
        var again = await CallAsync(_owner, (s, c) => s.CancelImport(new CancelImportRequest { UploadId = running }, c));

        Assert.Equal((ImportState.Cancelled, 250_000), (reply.Import.State, reply.Import.CostUsdMicros));
        Assert.Equal(ImportState.Cancelled, again.Import.State);
        Assert.Empty((await CallAsync(_owner, (s, c) => s.ListDocuments(new ListDocumentsRequest(), c))).Documents);
        var progress = await CallAsync(_owner, (s, c) => s.GetImportProgress(
            new GetImportProgressRequest { UploadIds = { running } }, c));
        Assert.Equal(ImportState.Cancelled, Assert.Single(progress.Imports).State);
    }

    [Fact]
    public async Task Only_the_owner_can_cancel_and_only_before_the_story_is_saved()
    {
        var theirs = await UploadAsync(_stranger, onDesk: true);
        var done = await UploadAsync(_owner, onDesk: false);
        await Ledger.ReportAsync(done, new RunProgress(RunPhase.Finished, "run", ResultId: Guid.NewGuid().ToString()), default);

        var notFound = await Assert.ThrowsAsync<RpcException>(() =>
            CallAsync(_owner, (s, c) => s.CancelImport(new CancelImportRequest { UploadId = theirs }, c)));
        var finished = await Assert.ThrowsAsync<RpcException>(() =>
            CallAsync(_owner, (s, c) => s.CancelImport(new CancelImportRequest { UploadId = done }, c)));

        Assert.Equal(StatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(StatusCode.FailedPrecondition, finished.StatusCode);
        Assert.Equal(RunPhase.Finished, (await Ledger.ProgressAsync([done], default))[done].Phase);
        Assert.Single((await CallAsync(_stranger, (s, c) => s.ListDocuments(new ListDocumentsRequest(), c))).Documents);
    }

    private async Task<string> UploadAsync(string userId, bool onDesk)
    {
        var id = DocumentImportIds.New();
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(userId);
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("seed");
        await scope.ServiceProvider.GetRequiredService<IDocumentUploadRepository>().AddAsync(new DocumentUploadRecord
        {
            UploadId = id, FileName = "tide.md", SizeBytes = 10, MediaType = "text/markdown", FileExtension = ".md",
        }, default);
        if (onDesk)
        {
            await scope.ServiceProvider.GetRequiredService<IDocumentRepository>().AddAsync(new DocumentRecord
            {
                Id = id, Name = "Tide", FileName = "tide.md", SizeBytes = 10, MediaType = "text/markdown",
                FileExtension = ".md", StorageKey = $"uploads/{id}.md",
            }, default);
        }

        await unitOfWork.CommitAsync(default);
        return id;
    }

    private async Task<TReply> CallAsync<TReply>(
        string userId, Func<DocumentService, ServerCallContext, Task<TReply>> call)
    {
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(userId);
        return await call(scope.ServiceProvider.GetRequiredService<DocumentService>(), new TestServerCallContext());
    }
}
