using System.Text.Json;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Pipeline.Agents;

namespace ProtoFast.Segmentation.ContractTests;

/// <summary>
/// The prompt assets are embedded resources, which means a renamed or unglobbed file fails at
/// runtime rather than at build time. These tests are what turn that into a build failure.
/// </summary>
public class PromptAssetTests
{
    private readonly PromptAssets _assets = new();

    [Theory]
    [InlineData("rules/common.md")]
    [InlineData("skills/layout-labeling/SKILL.md")]
    [InlineData("skills/heading-levels/SKILL.md")]
    [InlineData("skills/hierarchy-inference/SKILL.md")]
    [InlineData("skills/structure-review/SKILL.md")]
    [InlineData("skills/tree-repair/SKILL.md")]
    [InlineData("skills/augment/key-points/SKILL.md")]
    [InlineData("schemas/labels.schema.json")]
    [InlineData("schemas/tree.schema.json")]
    [InlineData("schemas/review.schema.json")]
    [InlineData("prompts/labeler.v1.md")]
    [InlineData("prompts/structurer.v1.md")]
    [InlineData("skills/structure-orchestration/SKILL.md")]
    [InlineData("schemas/structure-window.schema.json")]
    [InlineData("schemas/assembly-plan.schema.json")]
    [InlineData("schemas/structure-answers.schema.json")]
    [InlineData("prompts/structure-window.v1.md")]
    [InlineData("prompts/structure-orchestrator.v1.md")]
    [InlineData("prompts/structure-followup.v1.md")]
    public void EveryAssetTheAgentsUseIsEmbedded(string path)
    {
        Assert.False(string.IsNullOrWhiteSpace(_assets.Read(path)));
    }

    [Theory]
    [InlineData("scanned-book")]
    [InlineData("legal-filing")]
    [InlineData("slide-export")]
    [InlineData("transcript")]
    public void EveryDetectableFamilyHasASkill(string family)
    {
        // FamilyDetector can return these, and a family with no skill would silently fall back to
        // the generic prompt — which is a quality regression nothing would notice.
        Assert.NotEqual(string.Empty, _assets.FamilySkill(family));
    }

    [Fact]
    public void AnUnknownFamilyHasNoSkillAndThatIsNotAnError()
    {
        Assert.Equal(string.Empty, _assets.FamilySkill(Core.Ingest.FamilyDetector.Unknown));
        Assert.Equal(string.Empty, _assets.FamilySkill("no-such-family"));
    }

    [Fact]
    public void TheRulesSayTheThingsValidationDependsOn()
    {
        var rules = _assets.Rules;

        // Each of these is enforced by a deterministic check. The rule text is what gives a model
        // a chance to comply before the check rejects it.
        Assert.Contains("exactly once", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trusted", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never both", rules, StringComparison.OrdinalIgnoreCase);
        // The prompt-injection rule (plan §24.1).
        Assert.Contains("data, not instruction", rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EachRoleHasItsOwnPromptVersion()
    {
        var versions = Enum.GetValues<AgentRole>().ToDictionary(r => r, _assets.VersionFor);

        Assert.All(versions.Values, v => Assert.Equal(16, v.Length));

        // The labeler and the structurer share only rules/common.md, so their versions must differ
        // — otherwise a change to one role's skill would invalidate the other's qualification.
        Assert.NotEqual(versions[AgentRole.Labeler], versions[AgentRole.Structurer]);
    }

    [Fact]
    public void ThePromptVersionIsStableAcrossInstances()
    {
        // It is the qualification lookup key and is stored on every artifact, so an unstable
        // value would invalidate every model's qualification on each worker restart.
        Assert.Equal(new PromptAssets().VersionFor(AgentRole.Labeler), _assets.VersionFor(AgentRole.Labeler));
    }

    [Fact]
    public void TemplatePlaceholdersAreFilledAndUnfilledOnesVanish()
    {
        var rendered = new PromptTemplate("A {{one}} B {{missing}} C")
            .Set("one", "filled")
            .Render();

        Assert.Contains("A filled B", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("labels")]
    [InlineData("heading-levels")]
    [InlineData("review")]
    [InlineData("augment-key-points")]
    [InlineData("structure-window")]
    [InlineData("assembly-plan")]
    [InlineData("structure-answers")]
    public void WireSchemasStayInsideTheDecoderSubset(string name)
    {
        // The provider rejects the whole request with a 400 when a structured-output schema uses
        // one of these, so a keyword slipping in here fails every call of that role rather than
        // degrading. Recursion is the one that will be tempting to re-add: a section tree is
        // naturally recursive, and the wire form spells the depth out instead.
        using var document = JsonDocument.Parse(_assets.WireSchema(name));

        foreach (var keyword in new[]
                 {
                     "oneOf", "not", "minimum", "maximum", "multipleOf",
                     "minLength", "maxLength", "maxItems", "pattern", "$schema",
                 })
        {
            Assert.DoesNotContain($"\"{keyword}\"", _assets.WireSchema(name), StringComparison.Ordinal);
        }

        AssertObjectsAreClosed(document.RootElement);
    }

    [Fact]
    public void TheTreeWireSchemaStaysUnderTheDecodersGrammarBudget()
    {
        // Measured against the provider, not guessed. A node's optional properties can arrive in
        // any subset and any order, so each node costs sum(C(o,k)*k!) shapes for o optional
        // properties — and because the decoder composes one level into the next, the depth-6
        // chain multiplies those costs rather than adding them. Where the ceiling falls:
        //
        //   optional/node   product    verdict
        //     3 (depth 6)     5.2M     accepted in 12.5s
        //     4 (depth 5)     285M     accepted in 34.5s
        //     5 (depth 4)     2.2B     accepted in 84.5s
        //     4 (depth 6)    18.6B     "Schema is too complex." after 83.8s
        //     5 (depth 5)     733B     "Schema is too complex." after 180.5s
        //
        // At depth 6 that leaves room for exactly three optional properties per node, which the
        // schema spends on headingLineId (inferred sections anchor to no line) and the
        // children/paragraphs pair (their xor needs oneOf, which the decoder rejects outright).
        // A fourth is the whole request failing for the structurer and tree-repair roles, with
        // nothing to see locally: adding one costs no test, no compile error, and no warning.
        // That is what this test is for.
        using var document = JsonDocument.Parse(_assets.WireSchema("tree"));

        foreach (var definition in document.RootElement.GetProperty("$defs").EnumerateObject())
        {
            var optional = definition.Value.GetProperty("properties").EnumerateObject().Count()
                - definition.Value.GetProperty("required").GetArrayLength();

            Assert.True(
                optional <= 3,
                $"'{definition.Name}' has {optional} optional properties; at depth 6 the decoder "
                + "accepts at most 3. Make one required, drop it, or shorten the node chain.");
        }
    }

    [Fact]
    public void TheWindowWireSchemaIsTheTreeWireSchemaPlusOpenQuestions()
    {
        // The window agent returns a tree, so its schema shares the tree's depth-6 node chain. A
        // copy rather than a reference, because the two roles must version independently — and a
        // copy drifts unless something says it may not. What it may add is the open-questions
        // array and nothing else; anything more would be a second, unmeasured grammar.
        using var tree = JsonDocument.Parse(_assets.WireSchema("tree"));
        using var window = JsonDocument.Parse(_assets.WireSchema("structure-window"));

        Assert.Equal(
            tree.RootElement.GetProperty("$defs").GetRawText(),
            window.RootElement.GetProperty("$defs").GetRawText());

        Assert.Equal(
            ["title", "headingLineId", "inferred", "children", "paragraphs"],
            tree.RootElement.GetProperty("$defs").GetProperty("node1").GetProperty("properties")
                .EnumerateObject().Select(p => p.Name));

        // Required, not optional: an optional property multiplies the decoder's grammar through
        // the whole node chain, and an agent with nothing to ask returns [].
        Assert.Contains(
            "openQuestions",
            window.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("assembly-plan")]
    [InlineData("structure-answers")]
    public void TheOrchestrationSchemasHaveNoOptionalProperties(string name)
    {
        // These are flat, so they are not near the grammar ceiling — but every field being
        // required is also what makes the parse total: the loop never has to ask whether a missing
        // `followUps` meant "none" or "the model forgot".
        using var document = JsonDocument.Parse(_assets.WireSchema(name));

        AssertNoOptionalProperties(document.RootElement);

        static void AssertNoOptionalProperties(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (element.TryGetProperty("properties", out var properties)
                && element.TryGetProperty("type", out var type)
                && type.GetString() == "object")
            {
                var required = element.TryGetProperty("required", out var list)
                    ? list.EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal)
                    : [];

                foreach (var property in properties.EnumerateObject())
                {
                    Assert.Contains(property.Name, required);
                }
            }

            foreach (var child in element.EnumerateObject())
            {
                if (child.Value.ValueKind == JsonValueKind.Object)
                {
                    AssertNoOptionalProperties(child.Value);
                }
            }
        }
    }

    [Fact]
    public void TheOrchestrationRolesHaveTheirOwnPromptVersions()
    {
        // Two new qualification keys (orchestrator plan §7). If either collided with the
        // structurer's, a change to one role's prompt would silently invalidate the other's
        // qualification row — or worse, fail to.
        var versions = Enum.GetValues<AgentRole>().ToDictionary(r => r, _assets.VersionFor);

        Assert.NotEqual(versions[AgentRole.Structurer], versions[AgentRole.StructureWindower]);
        Assert.NotEqual(versions[AgentRole.Structurer], versions[AgentRole.StructureOrchestrator]);
        Assert.NotEqual(versions[AgentRole.StructureWindower], versions[AgentRole.StructureOrchestrator]);
    }

    [Fact]
    public void TheOrchestrationSkillForbidsWhatTheMaterializerRejects()
    {
        // Each of these is an exact error the materializer can return. The skill text is what
        // gives the orchestrator a chance to comply before it costs a repair round.
        var skill = _assets.Skill("structure-orchestration");

        Assert.Contains("exactly one section", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never both", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("document order", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not retitle", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(":n0", skill, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("labels")]
    [InlineData("heading-levels")]
    [InlineData("review")]
    [InlineData("augment-key-points")]
    public void WireSchemasNameTheSamePropertiesAsTheOnesThePromptShows(string name)
    {
        // Two files describing one artifact is the cost of the split: the prompt renders the
        // expressive schema and the decoder enforces the wire one, so a field added to either and
        // forgotten in the other is a model told one thing and constrained to another.
        using var authored = JsonDocument.Parse(_assets.Schema(name));
        using var wire = JsonDocument.Parse(_assets.WireSchema(name));

        Assert.Equal(PropertyNames(authored.RootElement), PropertyNames(wire.RootElement));
    }

    private static SortedSet<string> PropertyNames(JsonElement element)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Walk(element);
        return names;

        void Walk(JsonElement node)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                    {
                        // "properties" is the only place a field name is declared; every other
                        // object is schema vocabulary, which the two forms are allowed to differ on.
                        if (property.NameEquals("properties") && property.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var field in property.Value.EnumerateObject())
                            {
                                names.Add(field.Name);
                            }
                        }

                        Walk(property.Value);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }

    private static void AssertObjectsAreClosed(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "object")
            {
                Assert.True(
                    element.TryGetProperty("additionalProperties", out var additional)
                    && additional.ValueKind == JsonValueKind.False,
                    "every object needs additionalProperties: false");
            }

            foreach (var property in element.EnumerateObject())
            {
                AssertObjectsAreClosed(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertObjectsAreClosed(item);
            }
        }
    }

    [Theory]
    [InlineData("structurer.v1")]
    [InlineData("tree-repair.v1")]
    [InlineData("structure-reviewer.v1")]
    [InlineData("augmenter.v1")]
    [InlineData("augment-reviewer.v1")]
    public void EveryPromptThatDemandsASchemaCarriesIt(string template)
    {
        // Naming a schema file in the prompt tells the model nothing: it has no filesystem. A
        // template that asks for "JSON matching the schema" has to render the schema itself.
        var text = _assets.Template(template);

        Assert.Contains("{{schema}}", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("{{schema}}", StringComparison.Ordinal) < text.LastIndexOf("schema", StringComparison.Ordinal),
            "the instruction to match the schema must come after the schema itself");
    }

    [Fact]
    public void TheLabelerPromptIsOrderedForCaching()
    {
        // Rules, then the skill, then document statistics, then the variable content: the stable
        // prefix has to be byte-identical across calls for provider prompt caching to hit
        // (plan §14.9).
        var template = _assets.Template("labeler.v1");

        var rules = template.IndexOf("{{rules}}", StringComparison.Ordinal);
        var skill = template.IndexOf("{{skill}}", StringComparison.Ordinal);
        var statistics = template.IndexOf("{{statistics}}", StringComparison.Ordinal);
        var lines = template.IndexOf("{{lines}}", StringComparison.Ordinal);

        Assert.True(rules < skill, "rules must precede the skill");
        Assert.True(skill < statistics, "the skill must precede document statistics");
        Assert.True(statistics < lines, "the variable content must come last");
    }
}
