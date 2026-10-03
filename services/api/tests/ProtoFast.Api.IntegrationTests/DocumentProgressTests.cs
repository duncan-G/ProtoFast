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
        var done = await UploadAsync(_owner, onDesk: false);
        var abandoned = await UploadAsync(_owner, onDesk: false);
        var theirs = await UploadAsync(_stranger, onDesk: true);
        var storyId = Guid.NewGuid().ToString();
        await Ledger.ReportAsync(analysing, new RunProgress(RunPhase.Running, "run", "scenes", Cost: 0.4125m), default);
        await Ledger.ReportAsync(done, new RunProgress(RunPhase.Finished, "run", ResultId: storyId), default);
        await Ledger.ReportAsync(theirs, new RunProgress(RunPhase.Running, "run", "library"), default);

        var reply = await CallAsync(_owner, (s, c) => s.GetImportProgress(
            new GetImportProgressRequest { UploadIds = { queued, analysing, done, abandoned, theirs } }, c));

        var imports = reply.Imports.ToDictionary(i => i.UploadId);
        Assert.Equal(new[] { queued, analysing, done }.Order(), imports.Keys.Order());
        Assert.Equal(ImportState.Queued, imports[queued].State);
        Assert.Equal((ImportState.Analysing, "scenes"), (imports[analysing].State, imports[analysing].Stage));
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
