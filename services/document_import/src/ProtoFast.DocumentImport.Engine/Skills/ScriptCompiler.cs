using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>
/// Compiles agent-written C# into the worker's own runtime. A script is a <c>public static class Script</c>
/// with <c>RunAsync(ScriptContext context)</c>; code is immutable by hash, so each compiles once per process.
/// </summary>
public sealed class ScriptCompiler(IRegistry registry)
{
    public const string EntryType = "Script";
    public const string EntryMethod = "RunAsync";

    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Globalization;
        global using System.Linq;
        global using System.Text;
        global using System.Text.Json;
        global using System.Text.Json.Nodes;
        global using System.Text.RegularExpressions;
        global using System.Threading.Tasks;
        global using ProtoFast.DocumentImport.Engine.Skills;
        global using ProtoFast.DocumentImport.Engine.Storage;
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);
    private static readonly SyntaxTree GlobalUsingsTree = CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions);
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(LoadReferences);

    private readonly ConcurrentDictionary<string, CompiledScript> _compiled = new(StringComparer.Ordinal);

    /// <exception cref="ScriptCompilationException">The source does not compile or names an API scripts may not use.</exception>
    internal CompiledScript Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, path: "script.cs");
        var compilation = CSharpCompilation.Create(
            $"script-{Guid.NewGuid():N}",
            [GlobalUsingsTree, tree],
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false,
                nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree == tree)
            .Select(d => d.ToString())
            .ToList();
        if (errors.Count == 0)
        {
            errors.AddRange(ScriptApiPolicy.Violations(compilation, tree));
        }

        if (errors.Count == 0 && EntryViolation(compilation) is { } entry)
        {
            errors.Add(entry);
        }

        if (errors.Count > 0)
        {
            throw new ScriptCompilationException(errors);
        }

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
        {
            throw new ScriptCompilationException(
                emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList());
        }

        image.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
        var method = assembly.GetType(EntryType)!.GetMethod(EntryMethod, BindingFlags.Public | BindingFlags.Static)!;
        return new CompiledScript(method);
    }

    internal void Remember(string hash, CompiledScript script) => _compiled.TryAdd(hash, script);

    internal async Task<CompiledScript> LoadAsync(string hash, CancellationToken ct)
    {
        if (_compiled.TryGetValue(hash, out var cached))
        {
            return cached;
        }

        await using var code = await registry.OpenCodeAsync(hash, ct);
        using var reader = new StreamReader(code);
        return _compiled.GetOrAdd(hash, Compile(await reader.ReadToEndAsync(ct)));
    }

    private static string? EntryViolation(CSharpCompilation compilation)
    {
        var script = compilation.GetTypeByMetadataName(EntryType);
        var context = compilation.GetTypeByMetadataName(typeof(ScriptContext).FullName!);
        var entries = script?.GetMembers(EntryMethod).OfType<IMethodSymbol>()
            .Where(m => m is { IsStatic: true, DeclaredAccessibility: Accessibility.Public, ReturnsVoid: false, Parameters.Length: 1 }
                        && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, context))
            .ToList() ?? [];

        return entries.Count == 1
            ? null
            : $"A script declares exactly one 'public static async Task<object?> {EntryMethod}(ScriptContext context)' " +
              $"on a 'public static class {EntryType}' outside any namespace.";
    }

    // The framework's reference set plus the engine, for ScriptContext and the ref types.
    private static IReadOnlyList<MetadataReference> LoadReferences()
    {
        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => Path.GetFileName(path) is var name
                           && (name.StartsWith("System.", StringComparison.Ordinal) || name is "System.dll" or "netstandard.dll" or "mscorlib.dll"));

        return platform
            .Append(typeof(ScriptContext).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
