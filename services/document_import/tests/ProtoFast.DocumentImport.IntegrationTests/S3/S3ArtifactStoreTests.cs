using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Data.S3;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using ProtoFast.DocumentImport.Storage;
using ProtoFast.Storage.Abstractions;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.S3;

public class S3ArtifactStoreTests(LocalStackFixture localStack) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ContractRef Markdown = new("markdown", 1);

    private ServiceProvider _services = null!;

    private IObjectStore Objects => _services.GetRequiredService<IObjectStore>();

    private S3ArtifactStore Store => new(Objects);

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection().AddLogging();
        localStack.AddObjectStorage(services, await localStack.CreateBucketAsync());
        _services = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task An_artifact_round_trips_with_its_contract()
    {
        var reference = await Store.PutAsync("run-1", "extract", Utf8("# Title"), Markdown, Ct);

        using var reader = new StreamReader(await Store.GetAsync(reference, Ct));
        Assert.Equal("# Title", await reader.ReadToEndAsync(Ct));
        Assert.Equal(Markdown, await Store.ContractOfAsync(reference, Ct));
    }

    [Fact]
    public async Task Writing_the_same_content_twice_returns_the_same_reference()
    {
        var first = await Store.PutAsync("run-1", "extract", Utf8("same"), Markdown, Ct);
        var second = await Store.PutAsync("run-1", "extract", Utf8("same"), Markdown, Ct);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_stage_id_cannot_add_path_segments()
    {
        var reference = await Store.PutAsync("run-1", "../uploads", Utf8("x"), Markdown, Ct);

        Assert.True(await Objects.ExistsAsync(ArtifactKeys.RunArtifact("run-1", "../uploads", reference.Hash), Ct));
        Assert.All(await Objects.ListAsync("runs/", Ct), k => Assert.StartsWith("runs/run-1/..%2Fuploads/", k));
    }

    [Fact]
    public async Task A_missing_artifact_is_not_found()
    {
        var missing = new ArtifactRef("run-1", "extract", "0000");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Store.GetAsync(missing, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Store.ContractOfAsync(missing, Ct));
    }

    private static MemoryStream Utf8(string content) => new(Encoding.UTF8.GetBytes(content));
}
