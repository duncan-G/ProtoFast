using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.OffTheLoop;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Seeker;
using DocumentKindRecord = ProtoFast.Data.OffTheLoop.Entities.DocumentKind;
using Loop = ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.IntegrationTests;

public class DocumentTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);
    private readonly StoredFiles _files = database.Services.GetRequiredService<StoredFiles>();

    [Fact]
    public async Task A_file_is_listed_once_it_has_reached_storage()
    {
        var (id, storageKey) = await _seeker.StartUploadAsync(DocumentKindRecord.Resume, "Jane Doe - v4-backend.pdf");

        var early = await Assert.ThrowsAsync<RpcException>(() => Complete(id));
        Assert.Equal(StatusCode.FailedPrecondition, early.StatusCode);
        Assert.Empty(await _seeker.ListDocumentsAsync());

        _files.Put(storageKey);
        await Complete(id);

        var listed = Assert.Single(await _seeker.ListDocumentsAsync());
        Assert.Equal(Loop.DocumentKind.Resume, listed.Kind);
        Assert.Equal("Jane Doe - v4-backend", listed.Name);
        Assert.Equal("application/pdf", listed.MediaType);
    }

    [Fact]
    public async Task An_application_keeps_the_resume_and_cv_sent_with_it()
    {
        var resume = await _seeker.UploadAsync(DocumentKindRecord.Resume, "resume.pdf");
        var cv = await _seeker.UploadAsync(DocumentKindRecord.Cv, "cv.docx");
        var application = NewApplication((await _seeker.CreateCompanyAsync("Halcyon Pay")).Id);
        application.ResumeId = resume.Id;
        application.CvId = cv.Id;

        var created = await _seeker.CreateApplicationAsync(application);

        var stored = Assert.Single(await _seeker.ListApplicationsAsync());
        Assert.Equal(resume.Id, stored.ResumeId);
        Assert.Equal(cv.Id, stored.CvId);

        created.ResumeId = cv.Id;
        var swapped = await Assert.ThrowsAsync<RpcException>(() => _seeker.UpdateApplicationAsync(created));
        Assert.Equal(StatusCode.InvalidArgument, swapped.StatusCode);
    }

    [Fact]
    public async Task A_resume_can_go_with_many_applications_until_it_is_deleted()
    {
        var resume = await _seeker.UploadAsync(DocumentKindRecord.Resume, "v4-backend.pdf");
        foreach (var name in new[] { "Halcyon Pay", "Northwind Labs" })
        {
            var application = NewApplication((await _seeker.CreateCompanyAsync(name)).Id);
            application.ResumeId = resume.Id;
            await _seeker.CreateApplicationAsync(application);
        }

        var storageKey = Assert.Single(_files.Keys(_seeker.UserId));
        await _seeker.Documents((s, c) => s.DeleteDocument(new DeleteDocumentRequest { DocumentId = resume.Id }, c));

        Assert.Empty(await _seeker.ListDocumentsAsync());
        Assert.All(await _seeker.ListApplicationsAsync(), a => Assert.Equal("", a.ResumeId));
        Assert.False(_files.Holds(storageKey));
    }

    [Fact]
    public async Task A_file_is_renamed_and_opened_under_its_own_name()
    {
        var cv = await _seeker.UploadAsync(DocumentKindRecord.Cv, "Jane Doe CV.pdf");

        var renamed = (await _seeker.Documents((s, c) => s.RenameDocument(
            new RenameDocumentRequest { DocumentId = cv.Id, Name = "  academic, 2026  " }, c))).Document;
        var link = (await _seeker.Documents((s, c) => s.OpenDocument(
            new OpenDocumentRequest { DocumentId = cv.Id }, c))).Url;

        Assert.Equal("academic, 2026", renamed.Name);
        Assert.Equal("Jane Doe CV.pdf", renamed.FileName);
        Assert.Contains($"offtheloop/{_seeker.UserId}/documents/{cv.Id}.pdf", link);
        Assert.EndsWith("name=Jane%20Doe%20CV.pdf", link);
    }

    [Fact]
    public async Task Another_users_files_read_as_not_found_and_cannot_go_with_an_application()
    {
        var stranger = new Seeker(database);
        var resume = await _seeker.UploadAsync(DocumentKindRecord.Resume, "resume.pdf");

        Task[] attempts =
        [
            stranger.Documents((s, c) => s.OpenDocument(new OpenDocumentRequest { DocumentId = resume.Id }, c)),
            stranger.Documents((s, c) => s.RenameDocument(
                new RenameDocumentRequest { DocumentId = resume.Id, Name = "mine" }, c)),
            stranger.Documents((s, c) => s.DeleteDocument(new DeleteDocumentRequest { DocumentId = resume.Id }, c)),
            stranger.Documents((s, c) => s.CompleteDocumentUpload(
                new Loop.CompleteDocumentUploadRequest { DocumentId = resume.Id }, c)),
        ];
        foreach (var attempt in attempts)
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => attempt);
            Assert.Equal(StatusCode.NotFound, error.StatusCode);
        }

        var application = NewApplication((await stranger.CreateCompanyAsync("Halcyon Pay")).Id);
        application.ResumeId = resume.Id;
        var borrowed = await Assert.ThrowsAsync<RpcException>(() => stranger.CreateApplicationAsync(application));
        Assert.Equal(StatusCode.InvalidArgument, borrowed.StatusCode);
        Assert.Single(await _seeker.ListDocumentsAsync());
    }

    private Task<Loop.CompleteDocumentUploadReply> Complete(string id) =>
        _seeker.Documents((s, c) => s.CompleteDocumentUpload(new Loop.CompleteDocumentUploadRequest { DocumentId = id }, c));
}
