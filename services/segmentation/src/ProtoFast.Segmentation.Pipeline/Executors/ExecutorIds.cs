namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Every executor's stable identity.
///
/// <para>These strings are written into checkpoints. Changing one strands every in-flight run
/// across a deploy, because the resumed workflow looks for an executor that no longer exists
/// (plan §13.2). They are constants in one file so that fact is unmissable.</para>
/// </summary>
public static class ExecutorIds
{
    public const string Ingest = "ingest";
    public const string Clean = "clean";
    public const string Triage = "triage";
    public const string Label = "label";
    public const string Assemble = "assemble";
    public const string Presentation = "presentation";
    public const string Structure = "structure";
    public const string Validate = "validate";
    public const string Items = "items";
    public const string Referents = "referents";
    public const string Scenes = "scenes";
    public const string SceneLinks = "scene-links";
    public const string StructureReview = "structure-review";
    public const string HumanGate = "human-gate";

    /// <summary>
    /// The request port's own id. It is a distinct executor in the graph from
    /// <see cref="HumanGate"/> — the executor writes the review_tasks row, the port is what the
    /// run suspends on — so they cannot share an id.
    /// </summary>
    public const string HumanGatePort = "human-gate-port";

    public const string GateResume = "gate-resume";
    public const string Freeze = "freeze";
    public const string Augment = "augment";
    public const string Publish = "publish";

    /// <summary>
    /// The phase an executor belongs to. MAF reports a failure by executor id, and the run's
    /// error has to name the phase a person would recognise — "Structure" rather than
    /// "structure" — so the mapping lives next to the ids it maps.
    /// </summary>
    public static Core.Model.PipelinePhase? PhaseFor(string executorId) => executorId switch
    {
        Ingest => Core.Model.PipelinePhase.Ingest,
        Clean => Core.Model.PipelinePhase.Clean,
        Triage => Core.Model.PipelinePhase.Triage,
        Label => Core.Model.PipelinePhase.Label,
        Assemble => Core.Model.PipelinePhase.Assemble,
        Presentation => Core.Model.PipelinePhase.ClassifyPresentation,
        Structure => Core.Model.PipelinePhase.InferStructure,
        Validate => Core.Model.PipelinePhase.Validate,
        Items => Core.Model.PipelinePhase.TypeItems,
        Referents => Core.Model.PipelinePhase.ResolveReferents,
        Scenes => Core.Model.PipelinePhase.CutScenes,
        SceneLinks => Core.Model.PipelinePhase.LinkScenes,
        StructureReview => Core.Model.PipelinePhase.ReviewStructure,
        HumanGate or HumanGatePort or GateResume => Core.Model.PipelinePhase.HumanGate,
        Freeze => Core.Model.PipelinePhase.Freeze,
        Augment => Core.Model.PipelinePhase.Augment,
        Publish => Core.Model.PipelinePhase.Publish,
        _ => null,
    };
}
