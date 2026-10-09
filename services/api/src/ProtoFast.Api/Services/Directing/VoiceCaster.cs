using System.Text;
using ProtoFast.Api.Services.Speech;
using ProtoFast.DocumentImport.Screenplay.Models;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;
using VoiceProfileRecord = ProtoFast.Data.ThePlot.Entities.VoiceProfile;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Casts a character from the speech provider's voice library. Code searches with what the sheet
/// says (gender, age, accent), loosening the search until something fits; the model picks among
/// those candidates by label. Voices the story already uses are left out, so the cast sounds apart.
/// </summary>
public sealed class VoiceCaster(
    ISpeechSynthesizer speech, ILanguageModelFactory models, DirectorOptions options, ILogger<VoiceCaster> logger)
{
    private const int Candidates = 24;

    private const string System = """
        You cast voices for an audio adaptation of a screenplay. Given a character's sheet and a
        numbered list of library voices, pick the one a listener would most believe as that
        character. Answer with one JSON object in exactly the shape requested and nothing else.
        """;

    /// <summary>The character's voice with a library voice cast, or null when the library has none to offer.</summary>
    public async Task<VoiceProfileRecord?> CastAsync(CharacterRecord character, StoryRecord story, CancellationToken ct)
    {
        var taken = story.Characters.Where(c => c.Id != character.Id).Select(c => c.Voice?.VoiceId)
            .Concat(story.Narrators.Select(n => n.Voice.VoiceId))
            .Append(character.Voice?.VoiceId)
            .OfType<string>()
            .ToHashSet();
        var candidates = await CandidatesAsync(character, taken, ct);
        if (candidates.Count == 0)
        {
            return null;
        }

        var chosen = candidates.Count == 1 ? candidates[0] : await ChooseAsync(character, candidates, ct);
        var voice = character.Voice ?? new VoiceProfileRecord();
        return new VoiceProfileRecord
        {
            Description = voice.Description,
            Baseline = voice.Baseline,
            VoiceId = chosen.Id,
            VoiceName = chosen.Name,
            PreviewUrl = chosen.PreviewUrl,
        };
    }

    private async Task<IReadOnlyList<SpeechVoice>> CandidatesAsync(CharacterRecord character, IReadOnlySet<string> taken, CancellationToken ct)
    {
        var identity = character.Appearance;
        var gender = VoiceFilters.Gender(identity?.Gender);
        var age = VoiceFilters.Age(identity?.Age);
        var accent = VoiceFilters.Accent(character.Voice?.Description, character.Description);
        VoiceQuery[] searches =
        [
            new("en", Gender: gender, Age: age, Accent: accent, Limit: 60),
            new("en", Gender: gender, Age: age, Limit: 60),
            new("en", Gender: gender, Limit: 60),
            new("en", Limit: 60),
        ];
        foreach (var search in searches.Distinct())
        {
            var found = (await speech.SearchVoicesAsync(search, ct)).Where(v => !taken.Contains(v.Id)).Take(Candidates).ToList();
            if (found.Count > 0)
            {
                return found;
            }
        }

        return [];
    }

    private async Task<SpeechVoice> ChooseAsync(CharacterRecord character, IReadOnlyList<SpeechVoice> candidates, CancellationToken ct)
    {
        var list = new StringBuilder();
        foreach (var (voice, index) in candidates.Select((v, i) => (v, i)))
        {
            list.Append("- v").Append(index + 1).Append(": ").Append(voice.Name);
            if (voice.Traits.Length > 0)
            {
                list.Append(" (").Append(voice.Traits).Append(')');
            }

            if (voice.Description.Length > 0)
            {
                list.Append(". ").Append(voice.Description);
            }

            list.AppendLine();
        }

        var identity = character.Appearance;
        var prompt = $$"""
            Character: {{character.Name}} ({{character.Kind}}).
            Who they are: {{character.Description ?? "(not written)"}}
            Age: {{identity?.Age ?? ""}}. Gender: {{identity?.Gender ?? ""}}.
            Voice: {{character.Voice?.Description ?? ""}}. Delivery: {{character.Voice?.Baseline ?? ""}}.
            Library voices:
            {{list}}
            Pick one by its label.
            Reply: {"voice":"v1"}
            """;

        var model = models.For(options.ModelClass);
        var reply = await model.CompleteAsync(System, prompt, ct);
        using var json = ModelJson.Parse(reply.Text);
        var label = ModelJson.Str(json.RootElement, "voice")?.Trim().TrimStart('v', 'V');
        if (int.TryParse(label, out var n) && n >= 1 && n <= candidates.Count)
        {
            return candidates[n - 1];
        }

        logger.LogWarning("{Model} picked no listed voice for {Character} ({Answer}); casting the library's first", model.ModelId, character.Id, reply.Text);
        return candidates[0];
    }
}
