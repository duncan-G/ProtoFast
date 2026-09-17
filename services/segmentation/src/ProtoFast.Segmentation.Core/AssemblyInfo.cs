using System.Runtime.CompilerServices;

// The unit suite tests a few internals directly — the run-id generator with a fixed clock and
// randomness, the level normalizer, the whitespace rule. They are internal because nothing
// outside this assembly should call them, not because they are unimportant: they are exactly the
// pieces whose behaviour the public surface depends on and cannot demonstrate on its own.
[assembly: InternalsVisibleTo("ProtoFast.Segmentation.UnitTests")]
[assembly: InternalsVisibleTo("ProtoFast.Segmentation.ContractTests")]
[assembly: InternalsVisibleTo("ProtoFast.Segmentation.IntegrationTests")]
