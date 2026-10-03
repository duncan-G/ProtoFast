using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Postgres;

public class PostgresDocumentFamilyGenerationsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _family = $"prose-{Guid.NewGuid():N}";

    private PostgresDocumentFamilyGenerations Store => new(postgres.Contexts, TimeProvider.System);

    [Fact]
    public async Task A_family_never_reset_is_generation_zero()
    {
        Assert.Equal(0, await Store.CurrentAsync(_family, Ct));
    }

    [Fact]
    public async Task Each_reset_starts_the_next_generation_even_when_resets_race()
    {
        Assert.Equal(1, await Store.ResetAsync(_family, Ct));

        var raced = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Store.ResetAsync(_family, Ct)));

        Assert.Equal([2, 3, 4, 5, 6], raced.Order());
        Assert.Equal(6, await Store.CurrentAsync(_family, Ct));
    }
}
