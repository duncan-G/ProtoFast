using System.Text.Json;

namespace ProtoFast.Segmentation.Cli;

/// <summary>Argument parsing and dispatch for <c>segctl</c>.</summary>
public static class Cli
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Heading-level accuracy is NaN when a run detected no headings at all — which is a real
        // and reportable outcome, not a bug, so the report has to be able to say so rather than
        // failing to serialize.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var options = ParseOptions(args.Skip(1));

        try
        {
            return args[0] switch
            {
                "segment" => await SegmentCommand.RunAsync(options),
                "evaluate" => await EvaluateCommand.RunAsync(options),
                "checks" => await ChecksCommand.RunAsync(options),
                _ => Unknown(args[0]),
            };
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage() => Console.WriteLine(
        """
        segctl — ProtoFast document segmentation

          segctl segment  --in <file.md> [--layout <file.layout.json>] [--out <dir>]
              Runs the deterministic phases (ingest, clean, triage, assemble, tree) over a local
              file and writes the artifacts to --out. No AWS, no database, no provider keys.

          segctl checks   --in <file.md> [--layout <file.layout.json>]
              Runs every deterministic validation check and prints the report. Exit code 1 if any
              check fails, so it can gate a script.

          segctl evaluate --gold <dir-or-file.json> [--json]
              Scores the deterministic pipeline against gold documents and prints the metrics of
              plan §26.2 per condition bucket. Exit code 1 if a document misses its thresholds.

        Options:
          --in <path>        the Markdown document to segment
          --layout <path>    the .layout.json sibling, when one exists
          --out <dir>        where to write artifacts (default: ./segctl-out)
          --gold <path>      a gold document, or a directory of them
          --json             machine-readable output
          --quiet            errors only
        """);

    internal static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pending = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null)
                {
                    // A flag followed by another flag is a boolean flag.
                    options[pending] = "true";
                }

                pending = arg[2..];
                continue;
            }

            if (pending is null)
            {
                throw new ArgumentException($"unexpected argument '{arg}'");
            }

            options[pending] = arg;
            pending = null;
        }

        if (pending is not null)
        {
            options[pending] = "true";
        }

        return options;
    }

    internal static string Require(this Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value != "true"
            ? value
            : throw new ArgumentException($"--{name} is required");

    internal static bool Has(this Dictionary<string, string> options, string name) =>
        options.ContainsKey(name);
}
