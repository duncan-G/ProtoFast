using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Data;

public class PostgresDocumentFamilyCatalogTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _family = $"invoice-{Guid.NewGuid():N}";

    private PostgresDocumentFamilyCatalog Catalog => new(postgres.Contexts, TimeProvider.System);

    [Fact]
    public async Task Executors_are_listed_in_the_order_added_and_adding_twice_is_a_no_op()
    {
        var first = new ExecutorRef("extractor", 1);
        var second = new ExecutorRef("extractor", 2);

        await Catalog.AddExecutorAsync(_family, first, Ct);
        await Catalog.AddExecutorAsync(_family, second, Ct);
        await Catalog.AddExecutorAsync(_family, first, Ct);

        Assert.Equal([first, second], await Catalog.ExecutorsAsync(_family, Ct));
        Assert.Empty(await Catalog.ExecutorsAsync($"{_family}-other", Ct));
    }

    [Fact]
    public async Task A_verifier_id_is_defined_once_per_family()
    {
        var spec = new VerifierSpec("clean", "extract", "No bad words.");

        await Catalog.AddVerifierAsync(_family, spec, Ct);
        await Catalog.AddVerifierAsync($"{_family}-other", spec, Ct);

        Assert.Equal([spec], await Catalog.VerifiersAsync(_family, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Catalog.AddVerifierAsync(_family, spec, Ct));
    }
}
