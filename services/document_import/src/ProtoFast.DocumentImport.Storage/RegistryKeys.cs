namespace ProtoFast.DocumentImport.Storage;

/// <summary>
/// Registry content is keyed by its SHA-256, never by an id the agent chose, so no id can shape a
/// key and identical content is stored once.
/// </summary>
public static class RegistryKeys
{
    public const string Prefix = "engine/";

    public static string Playbook(string hash) => $"{Prefix}playbooks/{hash}.json";

    public static string Executor(string hash) => $"{Prefix}executors/{hash}.json";

    public static string Workflow(string hash) => $"{Prefix}workflows/{hash}.json";

    public static string Code(string hash) => $"{Prefix}code/{hash}";
}
