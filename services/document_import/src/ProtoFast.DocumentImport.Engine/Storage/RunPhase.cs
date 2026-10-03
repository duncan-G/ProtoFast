namespace ProtoFast.DocumentImport.Engine.Storage;

/// <summary>Where the work on one source stands, as the person waiting on it is shown.</summary>
public enum RunPhase
{
    /// <summary>Turning the source into the run's input, before any run is open.</summary>
    Preparing,
    Running,
    /// <summary>The run passed; its output is being saved.</summary>
    Finishing,
    Finished,
    /// <summary>An attempt failed and the source will be tried again.</summary>
    Retrying,
    Failed,
}
