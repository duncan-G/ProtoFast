using System.Text.Json;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using Xunit;
using static ProtoFast.DocumentImport.UnitTests.Engine.EngineHarness;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class SkillRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string CountLines = """
        public static class Script
        {
            public static async Task<object?> RunAsync(ScriptContext context)
            {
                var text = await context.ReadTextAsync(context.Arg<ArtifactRef>("artifact"));
                return new { lines = text.Split('\n').Length };
            }
        }
        """;

    private readonly EngineHarness _h = new(o => o.ScriptTimeout = TimeSpan.FromMilliseconds(300));

    private async Task<(string RunId, ArtifactRef Input, SkillRuntime Runtime)> OpenRunAsync(string content = "one\ntwo\nthree")
    {
        var runId = DocumentImport.Core.DocumentImportIds.New();
        var input = await _h.InputAsync(content);
        await _h.Ledger.OpenAsync(runId, _h.DocumentSignature, RunMode.Discovery, Ct);
        var tools = _h.Get<AgentToolsFactory>().ForRun(runId, _h.DocumentSignature, input, new TraceRef(runId), Ct);
        return (runId, input, _h.Get<SkillRuntimeFactory>().Create(tools, Ct));
    }

    private static JsonElement Args(object value) => SkillJson.ToElement(value);

    private static async Task<JsonElement> OkAsync(Task<SkillResult> run)
    {
        var result = await run;
        Assert.False(result.IsError, result.Content);
        return JsonDocument.Parse(result.Content).RootElement;
    }

    private static Task CreateLineCountAsync(SkillRuntime runtime) =>
        OkAsync(runtime.RunAsync(BuiltInSkills.CreateSkill, "create", Args(new
        {
            name = "line-count",
            description = "Count an artifact's lines.",
            instructions = "Run `count` with { artifact }.",
        })));

    [Fact]
    public async Task The_engine_primitives_and_the_two_creators_are_built_in()
    {
        var (_, _, runtime) = await OpenRunAsync();

        var skills = await runtime.ListAsync();
        var create = await runtime.LoadAsync(BuiltInSkills.CreateCode);

        Assert.All(skills, s => Assert.True(s.BuiltIn));
        Assert.Equal(
            ["context", "read-artifact", "write-artifact", "define-playbook", "define-executor", "define-verifier",
             "delegate", "record-decision", "create-skill", "create-code", "remove-skill"],
            skills.Select(s => s.Id));
        Assert.All(skills, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
        Assert.Contains("public static class Script", create.Content);
        Assert.DoesNotContain("description:", create.Content);
    }

    [Fact]
    public async Task A_skill_and_its_code_are_kept_for_the_next_run_of_the_family()
    {
        var (_, _, first) = await OpenRunAsync();
        await CreateLineCountAsync(first);
        var created = await OkAsync(first.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count", script = "count", description = "{ artifact } -> { lines }", source = CountLines,
        })));
        Assert.Equal(2, created.GetProperty("skill").GetProperty("version").GetInt32());

        var (_, input, next) = await OpenRunAsync();
        var mine = Assert.Single(await next.ListAsync(), s => !s.BuiltIn);
        var loaded = await next.LoadAsync("line-count");
        var counted = await OkAsync(next.RunAsync("line-count", "count", Args(new { artifact = input })));

        Assert.Equal(("line-count", "Count an artifact's lines."), (mine.Id, mine.Description));
        Assert.Contains("# line-count (version 2)", loaded.Content);
        Assert.Contains("text.Split('\\n')", loaded.Content);
        Assert.Equal(3, counted.GetProperty("lines").GetInt32());
    }

    [Fact]
    public async Task Updating_a_skill_keeps_its_scripts()
    {
        var (_, input, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);
        await OkAsync(runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count", script = "count", description = "counts", source = CountLines,
        })));

        await CreateLineCountAsync(runtime);

        await OkAsync(runtime.RunAsync("line-count", "count", Args(new { artifact = input })));
    }

    [Fact]
    public async Task A_script_records_a_stage_through_the_built_ins()
    {
        var (runId, input, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);
        await OkAsync(runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count",
            script = "write",
            description = "{ artifact } -> writes stage 'lines'",
            source = """
                public static class Script
                {
                    public static async Task<object?> RunAsync(ScriptContext context)
                    {
                        var artifact = context.Arg<ArtifactRef>("artifact");
                        var text = await context.ReadTextAsync(artifact);
                        return await context.RunAsync("write-artifact", "write-artifact", new
                        {
                            stageId = "lines",
                            contract = new { schemaId = "markdown", version = 1 },
                            content = $"{text.Split('\n').Length} lines",
                            inputs = new[] { artifact },
                        });
                    }
                }
                """,
        })));
        runtime.Spend(new Cost(0.25m, TimeSpan.FromSeconds(1)));

        var written = await OkAsync(runtime.RunAsync("line-count", "write", Args(new { artifact = input })));

        var record = Assert.Single((await _h.Ledger.SummariseAsync(runId, Ct)).Stages);
        Assert.Equal(("lines", Tier.Orchestrator), (record.StageId, record.Tier));
        Assert.Equal([input], record.Inputs);
        Assert.Equal("3 lines", await _h.ReadAsync(record.Output));
        Assert.Equal(0.25m, record.Result.Cost.Amount);
        Assert.True(written.GetProperty("passed").GetBoolean());
        Assert.True(runtime.HasPassed("lines"));
    }

    [Fact]
    public async Task A_skill_tied_to_one_document_is_refused_with_what_to_fix()
    {
        var (_, _, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);

        var instructions = await runtime.RunAsync(BuiltInSkills.CreateSkill, "create", Args(new
        {
            name = "line-count", description = "Count lines.", instructions = "Sweetfoot has 120 lines.",
        }));
        var code = await runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count", script = "sweetfoot", description = "counts",
            source = CountLines.Replace("text.Split", "\"Sweetfoot\".Length + text.Split"),
        }));

        Assert.True(instructions.IsError);
        Assert.Equal("Skill 'line-count' was not published: no-sweetfoot failed. Tied to Sweetfoot.\n- instructions: names Sweetfoot", instructions.Content);
        Assert.True(code.IsError);
        Assert.EndsWith("- scripts/sweetfoot: names Sweetfoot", code.Content);
        var loaded = (await runtime.LoadAsync("line-count")).Content;
        Assert.Contains("(version 1)", loaded);
        Assert.DoesNotContain("Sweetfoot", loaded);
    }

    [Fact]
    public async Task Updating_a_skill_can_drop_scripts()
    {
        var (_, input, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);
        await OkAsync(runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count", script = "count", description = "counts", source = CountLines,
        })));

        var unknown = await runtime.RunAsync(BuiltInSkills.CreateSkill, "create", Args(new
        {
            name = "line-count", description = "Count lines.", instructions = "i", remove = new[] { "tally" },
        }));
        await OkAsync(runtime.RunAsync(BuiltInSkills.CreateSkill, "create", Args(new
        {
            name = "line-count", description = "Count lines.", instructions = "i", remove = new[] { "count" },
        })));
        var run = await runtime.RunAsync("line-count", "count", Args(new { artifact = input }));

        Assert.Contains("has no script 'tally' to remove", unknown.Content);
        Assert.True(run.IsError);
        Assert.Contains("has no script 'count'", run.Content);
        Assert.Contains("removed `count`", string.Join('\n', runtime.TakeEffects().Select(e => e.Summary)));
    }

    [Fact]
    public async Task A_removed_skill_is_gone_from_later_runs_but_kept_in_the_family_history()
    {
        var (_, input, first) = await OpenRunAsync();
        await CreateLineCountAsync(first);
        await OkAsync(first.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count", script = "count", description = "counts", source = CountLines,
        })));

        var removed = await OkAsync(first.RunAsync(BuiltInSkills.RemoveSkill, "remove", Args(new
        {
            name = "line-count", reason = "Only fit one manuscript.",
        })));
        var again = await first.RunAsync(BuiltInSkills.RemoveSkill, "remove", Args(new { name = "line-count", reason = "r" }));
        var (_, _, next) = await OpenRunAsync();
        var listed = await next.ListAsync();
        var run = await next.RunAsync("line-count", "count", Args(new { artifact = input }));
        await CreateLineCountAsync(next);
        var fresh = await next.LoadAsync("line-count");

        Assert.Equal("line-count", removed.GetProperty("removed").GetString());
        Assert.Contains("no skill 'line-count' of yours to remove", again.Content);
        Assert.Contains("Removed line-count@2: Only fit one manuscript.", string.Join('\n', first.TakeEffects().Select(e => e.Summary)));
        Assert.All(listed, s => Assert.True(s.BuiltIn));
        Assert.Contains("There is no skill 'line-count'", run.Content);
        Assert.Contains("# line-count (version 3)", fresh.Content);
        Assert.DoesNotContain("- `count`", fresh.Content);
        Assert.Equal(
            [new SkillRef("line-count", 3)],
            await _h.Get<IDocumentFamilyCatalog>().SkillsAsync(_h.DocumentSignature.Family, Ct));
    }

    [Theory]
    [InlineData("remove-skill", "remove", """{ "name": "missing", "reason": "r" }""", "no skill 'missing' of yours to remove")]
    [InlineData("remove-skill", "remove", """{ "name": "missing", "reason": " " }""", "Say why")]
    [InlineData("nope", "run", "{}", "There is no skill 'nope'.")]
    [InlineData("delegate", "run", "{}", "has one script, 'delegate'")]
    [InlineData("write-artifact", "write-artifact", "{}", "'stageId' is required.")]
    [InlineData("create-skill", "create", """{ "name": "delegate", "description": "d", "instructions": "i" }""", "not a usable skill name")]
    [InlineData("create-skill", "create", """{ "name": "Line Count", "description": "d", "instructions": "i" }""", "not a usable skill name")]
    [InlineData("create-code", "create", """{ "skill": "missing", "script": "s", "description": "d", "source": "" }""", "create it with create-skill first")]
    public async Task Mistakes_go_back_to_the_agent(string skill, string script, string args, string message)
    {
        var (_, _, runtime) = await OpenRunAsync();

        var result = await runtime.RunAsync(skill, script, JsonDocument.Parse(args).RootElement);

        Assert.True(result.IsError);
        Assert.Contains(message, result.Content);
    }

    [Theory]
    [InlineData("""throw new InvalidOperationException("no scenes");""", "threw InvalidOperationException: no scenes")]
    [InlineData("""await Task.Delay(10_000);""", "timed out")]
    [InlineData("""await context.RunAsync("line-count", "boom");""", "line-count/boom failed")]
    public async Task A_failing_script_is_an_error_result(string statements, string message)
    {
        var (_, _, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);
        await OkAsync(runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count",
            script = "boom",
            description = "fails",
            source = $$"""
                public static class Script
                {
                    public static async Task<object?> RunAsync(ScriptContext context)
                    {
                        await Task.Yield();
                        {{statements}}
                        return null;
                    }
                }
                """,
        })));

        var result = await runtime.RunAsync("line-count", "boom", Args(new { }));

        Assert.True(result.IsError);
        Assert.Contains(message, result.Content);
    }

    [Fact]
    public async Task Code_that_names_the_host_is_never_stored()
    {
        var (_, _, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);

        var result = await runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count",
            script = "leak",
            description = "reads the environment",
            source = """
                public static class Script
                {
                    public static Task<object?> RunAsync(ScriptContext context) =>
                        Task.FromResult<object?>(Environment.GetEnvironmentVariables());
                }
                """,
        }));

        Assert.True(result.IsError);
        Assert.Contains("'System.Environment' is not available to scripts.", result.Content);
        Assert.DoesNotContain("`leak`", (await runtime.LoadAsync("line-count")).Content);
    }

    [Fact]
    public async Task Code_the_safety_review_refuses_is_never_stored()
    {
        var (_, _, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);

        var result = await runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count", script = "tally", description = "counts", source = CountLines.Replace("return", "// exfiltrate\n        return"),
        }));

        Assert.True(result.IsError);
        Assert.Equal("Script 'tally' was refused: It copies data out of the run.\n- line 5: exfiltrates", result.Content);
        var review = Assert.Single(_h.ScriptReviewer.Reviews);
        Assert.Equal(("line-count", "tally", "counts"), (review.Skill, review.Script, review.Description));
        Assert.DoesNotContain("`tally`", (await runtime.LoadAsync("line-count")).Content);
        Assert.Equal(0.02m, runtime.DrainSpend()?.Amount);
    }

    [Fact]
    public async Task Code_is_refused_when_the_safety_review_faults()
    {
        var (_, _, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);

        var result = await runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count", script = "tally", description = "counts", source = CountLines.Replace("return", "// unreviewable\n        return"),
        }));

        Assert.True(result.IsError);
        Assert.Equal("Script 'tally' was refused: it could not be checked. Try again later.", result.Content);
        Assert.DoesNotContain("`tally`", (await runtime.LoadAsync("line-count")).Content);
    }

    [Fact]
    public async Task Code_that_does_not_compile_is_not_reviewed_and_the_agent_is_never_told_of_the_review()
    {
        var (_, _, runtime) = await OpenRunAsync();
        await CreateLineCountAsync(runtime);

        var result = await runtime.RunAsync(BuiltInSkills.CreateCode, "create", Args(new
        {
            skill = "line-count", script = "count", description = "counts", source = "not C#",
        }));

        Assert.True(result.IsError);
        Assert.Empty(_h.ScriptReviewer.Reviews);
        foreach (var skill in (await runtime.ListAsync()).Select(s => s.Id))
        {
            Assert.DoesNotContain("safety", (await runtime.LoadAsync(skill)).Content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
