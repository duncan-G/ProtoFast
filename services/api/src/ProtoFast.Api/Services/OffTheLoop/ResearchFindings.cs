using System.Text.Json;
using ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>The model's answer, unchecked, and the pages it drew on.</summary>
public sealed record ResearchFindings(JsonElement Answer, IReadOnlyList<WebSource> Sources) : ResearchUpdate;
