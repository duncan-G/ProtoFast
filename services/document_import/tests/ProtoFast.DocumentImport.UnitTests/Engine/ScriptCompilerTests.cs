using ProtoFast.DocumentImport.Engine.Skills;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class ScriptCompilerTests
{
    private readonly ScriptCompiler _compiler = new EngineHarness().Get<ScriptCompiler>();

    private static string Body(string statements) => $$"""
        public static class Script
        {
            public static async Task<object?> RunAsync(ScriptContext context)
            {
                await Task.Yield();
                {{statements}}
                return null;
            }
        }
        """;

    [Fact]
    public void Computation_over_text_collections_and_json_compiles()
    {
        _compiler.Compile("""
            public static class Script
            {
                public static async Task<object?> RunAsync(ScriptContext context)
                {
                    var text = await context.ReadTextAsync(context.Arg<ArtifactRef>("artifact"));
                    var words = Regex.Matches(text, @"\w+").Select(m => m.Value.ToLowerInvariant());
                    var counts = new Dictionary<string, int>();
                    foreach (var word in words)
                    {
                        counts[word] = counts.GetValueOrDefault(word) + 1;
                    }

                    var top = counts.OrderByDescending(c => c.Value).Take(3).Select(c => new Tally(c.Key, c.Value)).ToList();
                    return new JsonObject { ["top"] = JsonSerializer.SerializeToNode(top), ["at"] = DateTime.UnixEpoch.ToString("O", CultureInfo.InvariantCulture) };
                }

                private sealed record Tally(string Word, int Count);
            }
            """);
    }

    [Theory]
    [InlineData("""var secret = System.IO.File.ReadAllText("/etc/passwd");""", "System.IO.File")]
    [InlineData("""var key = Environment.GetEnvironmentVariable("Providers__anthropic__ApiKey");""", "System.Environment")]
    [InlineData("""var name = typeof(string).Assembly.FullName;""", "System.Type")]
    [InlineData("""var name = "".GetType().Name;""", "System.Type")]
    [InlineData("""System.Diagnostics.Process.Start("sh");""", "System.Diagnostics.Process")]
    [InlineData("""var http = new System.Net.Http.HttpClient();""", "System.Net.Http.HttpClient")]
    [InlineData("""var path = AppContext.BaseDirectory;""", "System.AppContext")]
    [InlineData("""System.Threading.Thread.Sleep(1);""", "System.Threading.Thread")]
    [InlineData("""dynamic value = 1; var text = value.ToString();""", "dynamic")]
    public void Scripts_cannot_name_the_host(string statements, string named)
    {
        var refused = Assert.Throws<ScriptCompilationException>(() => _compiler.Compile(Body(statements)));

        Assert.Contains(refused.Errors, e => e.Contains(named));
    }

    [Fact]
    public void Native_calls_are_refused()
    {
        var refused = Assert.Throws<ScriptCompilationException>(() => _compiler.Compile("""
            public static class Script
            {
                [System.Runtime.InteropServices.DllImport("libc")]
                private static extern int getpid();

                public static Task<object?> RunAsync(ScriptContext context) => Task.FromResult<object?>(getpid());
            }
            """));

        Assert.Contains(refused.Errors, e => e.Contains("'extern' is not allowed"));
    }

    [Fact]
    public void Compiler_errors_come_back_with_their_positions()
    {
        var refused = Assert.Throws<ScriptCompilationException>(() => _compiler.Compile(Body("var x = ;")));

        Assert.Contains(refused.Errors, e => e.StartsWith("script.cs(6,") && e.Contains("error CS"));
    }

    [Fact]
    public void A_script_needs_its_entry_point()
    {
        var refused = Assert.Throws<ScriptCompilationException>(() => _compiler.Compile("public static class Helpers { }"));

        Assert.Contains("RunAsync(ScriptContext context)", Assert.Single(refused.Errors));
    }
}
