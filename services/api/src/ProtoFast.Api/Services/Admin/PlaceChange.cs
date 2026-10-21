namespace ProtoFast.Api.Services.Admin;

/// <param name="Locations">The locations it changes: an object's place, or both ends of a way.</param>
internal sealed record PlaceChange(int Moment, IReadOnlyList<string> Locations, string Text);
