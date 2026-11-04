using System.Text.Json;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// The answer is JSON matching <paramref name="Schema"/>; with <paramref name="Web"/> the model may search and read links.
/// A <paramref name="File"/> goes with the request as it is.
/// </summary>
public sealed record ResearchBrief(string System, string User, JsonElement Schema, bool Web, ResearchFile? File = null);
