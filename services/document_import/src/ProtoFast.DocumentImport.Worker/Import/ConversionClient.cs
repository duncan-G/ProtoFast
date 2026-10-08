using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ProtoFast.DocumentImport.Worker.Import;

public sealed class ConversionClient(IHttpClientFactory httpClients, IOptions<ConversionOptions> options)
{
    public const string HttpClientName = "conversion";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.BaseUrl);

    /// <summary>Returns the key the Markdown was written to.</summary>
    public async Task<string> ConvertAsync(ConvertRequest request, CancellationToken ct)
    {
        var http = httpClients.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(options.Value.BaseUrl!.TrimEnd('/') + "/");

        using var response = await http.PostAsJsonAsync("convert", request, Json, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Conversion returned {(int)response.StatusCode}: {body}", null, response.StatusCode);
        }

        var reply = JsonSerializer.Deserialize<ConvertReply>(body, Json)
            ?? throw new HttpRequestException("Conversion returned an empty reply.");
        return reply.MarkdownKey;
    }
}
