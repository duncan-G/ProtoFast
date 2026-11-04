using ProtoFast.Api.Services.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class ModelProviderTests
{
    [Fact]
    public void Cached_input_is_priced_at_its_multiple_of_the_input_price()
    {
        var gemini = new ModelProviders().Gemini;

        var cost = gemini.PriceOf("gemini-3.8-flash", new ModelUsage(InputTokens: 1_000_000, CachedInputTokens: 400_000, OutputTokens: 200_000));

        Assert.Equal(0.75m * 0.64m + 3.75m * 0.2m, cost);
    }

    [Fact]
    public void A_model_without_prices_costs_nothing()
    {
        var gemini = new ModelProviders().Gemini;

        Assert.Equal(0m, gemini.PriceOf("gemini-unpriced", new ModelUsage(1_000, 0, 1_000)));
    }
}
