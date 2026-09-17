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
