// Microsoft.Extensions.AI ships its own RoutingContext, which means something quite different
// from ours (this one is "what the agent is asking the router for", plan §14.5). An explicit
// alias here settles it once, rather than every file that touches both an IChatClient and the
// router having to disambiguate.
global using RoutingContext = ProtoFast.Segmentation.Routing.RoutingContext;

// Same story for Run: MAF's Run is a workflow execution, ours is a database row for one
// document's pass through the pipeline. Executors mention both, so the alias picks the one that
// appears far more often in this assembly.
global using SegmentationRun = ProtoFast.Segmentation.Data.Entities.Run;
