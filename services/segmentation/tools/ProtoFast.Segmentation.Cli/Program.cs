using ProtoFast.Segmentation.Cli;

// segctl — the operator tool of plan §27.
//
// It runs the deterministic half of the pipeline against a local file and scores it against the
// gold set, with no AWS, no Postgres and no provider keys. That is deliberate: the checks and
// metrics are pure functions (plan §11, §26.2), so the fastest way to see what a change did is to
// run them directly rather than through a queue.
return await Cli.RunAsync(args);
