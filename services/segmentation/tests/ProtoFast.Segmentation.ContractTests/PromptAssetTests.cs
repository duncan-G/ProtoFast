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
