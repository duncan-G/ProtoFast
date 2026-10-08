using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>
/// Scripts run in the worker's process with its secrets and network, and the agent that writes
/// them reads untrusted documents. So a script may only touch computation: text, collections,
/// LINQ, JSON and the <see cref="ScriptContext"/>. Not a sandbox; it bounds what the source can name.
/// </summary>
internal static class ScriptApiPolicy
{
    private static readonly HashSet<string> AllowedNamespaces = new(StringComparer.Ordinal)
    {
        "System",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Collections.Generic",
        "System.Collections.Immutable",
        "System.Collections.ObjectModel",
        "System.Collections.Specialized",
        "System.Diagnostics.CodeAnalysis",
        "System.Globalization",
        "System.Linq",
        "System.Numerics",
        "System.Text",
        "System.Text.Json",
        "System.Text.Json.Nodes",
        "System.Text.Json.Serialization",
        "System.Text.RegularExpressions",
        "System.Threading.Tasks",
    };

    private static readonly HashSet<string> DeniedTypes = new(StringComparer.Ordinal)
    {
        "System.Activator",
        "System.AppContext",
        "System.AppDomain",
        "System.ArgIterator",
        "System.Console",
        "System.Environment",
        "System.GC",
        "System.RuntimeFieldHandle",
        "System.RuntimeMethodHandle",
        "System.RuntimeTypeHandle",
        "System.Type",
        "System.TypedReference",
    };

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        "System.IO.StringReader",
        "System.IO.StringWriter",
        "System.IO.TextReader",
        "System.IO.TextWriter",
        "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable",
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable",
        "System.Runtime.CompilerServices.YieldAwaitable",
        "System.Threading.CancellationToken",
        typeof(ScriptContext).FullName!,
        typeof(ArtifactRef).FullName!,
        typeof(ContractRef).FullName!,
    };

    private static readonly HashSet<SyntaxKind> DeniedKeywords =
    [
        SyntaxKind.ExternKeyword,
        SyntaxKind.UnsafeKeyword,
        SyntaxKind.FixedKeyword,
        SyntaxKind.StackAllocKeyword,
        SyntaxKind.ArgListKeyword,
        SyntaxKind.MakeRefKeyword,
        SyntaxKind.RefTypeKeyword,
        SyntaxKind.RefValueKeyword,
    ];

    public static IReadOnlyList<string> Violations(CSharpCompilation compilation, SyntaxTree tree)
    {
        var model = compilation.GetSemanticModel(tree);
        var violations = new List<string>();

        foreach (var token in tree.GetRoot().DescendantTokens().Where(t => DeniedKeywords.Contains(t.Kind())))
        {
            violations.Add(At(token.GetLocation(), $"'{token.Text}' is not allowed in a script."));
        }

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            var info = model.GetSymbolInfo(node);
            var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            var type = model.GetTypeInfo(node).Type;
            var reason = (symbol is null ? null : SymbolViolation(symbol, compilation))
                ?? (type is null ? null : TypeViolation(type, compilation));
            if (reason is not null)
            {
                violations.Add(At(node.GetLocation(), reason));
            }
        }

        return violations.Distinct().ToList();
    }

    private static string? SymbolViolation(ISymbol symbol, Compilation compilation) => symbol switch
    {
        INamespaceSymbol => null,
        ITypeSymbol type => TypeViolation(type, compilation),
        IMethodSymbol method =>
            TypeViolation((method.ReducedFrom ?? method).ContainingType, compilation)
            ?? TypeViolation(method.ReturnType, compilation)
            ?? method.TypeArguments.Select(t => TypeViolation(t, compilation)).FirstOrDefault(v => v is not null),
        IPropertySymbol property => TypeViolation(property.ContainingType, compilation) ?? TypeViolation(property.Type, compilation),
        IFieldSymbol field => TypeViolation(field.ContainingType, compilation) ?? TypeViolation(field.Type, compilation),
        IEventSymbol @event => TypeViolation(@event.ContainingType, compilation),
        ILocalSymbol local => TypeViolation(local.Type, compilation),
        IParameterSymbol parameter => TypeViolation(parameter.Type, compilation),
        _ => null,
    };

    private static string? TypeViolation(ITypeSymbol type, Compilation compilation)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return TypeViolation(array.ElementType, compilation);
            case IPointerTypeSymbol or IFunctionPointerTypeSymbol:
                return "Pointers are not allowed in a script.";
            case IDynamicTypeSymbol:
                return "'dynamic' is not allowed in a script.";
            case ITypeParameterSymbol or IErrorTypeSymbol:
                return null;
            case INamedTypeSymbol named:
                if (!named.IsAnonymousType && !named.IsTupleType
                    && !SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly))
                {
                    var outermost = named.OriginalDefinition;
                    while (outermost.ContainingType is { } containing)
                    {
                        outermost = containing;
                    }

                    var name = $"{outermost.ContainingNamespace.ToDisplayString()}.{outermost.MetadataName}";
                    var nameWithoutArity = name.Split('`')[0];
                    if (!AllowedTypes.Contains(nameWithoutArity)
                        && (DeniedTypes.Contains(nameWithoutArity) || !AllowedNamespaces.Contains(outermost.ContainingNamespace.ToDisplayString())))
                    {
                        return $"'{nameWithoutArity}' is not available to scripts.";
                    }
                }

                return named.TypeArguments.Select(t => TypeViolation(t, compilation)).FirstOrDefault(v => v is not null);
            default:
                return null;
        }
    }

    private static string At(Location location, string message)
    {
        var line = location.GetLineSpan().StartLinePosition;
        return $"({line.Line + 1},{line.Character + 1}): {message}";
    }
}
