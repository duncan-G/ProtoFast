namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>What an earlier delivery of the same build already did, so this one carries on from there.</summary>
/// <param name="Flashbacks">Scenes already read as flashbacks.</param>
public sealed record BuildResume(IReadOnlySet<Guid> HandledScenes, int NextSequence, bool Seeded, IReadOnlySet<Guid> Flashbacks);
