namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <param name="Text">A period detail a camera could show or a microphone hear: dress, weapons, tools, weather, light, sound.</param>
public sealed record BriefDetail(string Text, IReadOnlyList<long>? Sources);
