using System.Text.RegularExpressions;
using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Reads the model's staging words into <see cref="DirectionVocabulary"/> labels, all as the viewer
/// sees the picture, and answers the questions the screen-direction check asks of them.
/// </summary>
public static partial class ScreenDirections
{
    public const string LeftToRight = "left to right";

    public const string RightToLeft = "right to left";

    public static string Side(string? answer) =>
        Match(DirectionVocabulary.ScreenSides, Normal(answer).Replace("screen ", "").Replace("center", "centre"));

    public static string Facing(string? answer)
    {
        var text = Normal(answer).Replace("towards", "toward").Replace("the camera", "camera");
        var side = text.Contains("left") ? "left" : text.Contains("right") ? "right" : null;
        if (text.Contains("three quarter") && side is not null)
        {
            var back = text.Contains("back") || text.Contains("rear") || text.Contains("away");
            return back ? $"back three-quarter {side}" : $"three-quarter {side}";
        }

        return text switch
        {
            "left" => "screen left",
            "right" => "screen right",
            "camera" or "front" or "forward" => "toward camera",
            "away" or "back" or "back to camera" => "away from camera",
            _ => Match(DirectionVocabulary.Facings, text),
        };
    }

    /// <summary>
    /// How a body turned this way cannot be going that way, or null: facing the camera while going
    /// away from it, a back to the camera while coming toward it, or facing against the way across.
    /// </summary>
    public static string? Contradiction(string facing, string moving)
    {
        if (facing.Length == 0 || moving.Length == 0)
        {
            return null;
        }

        var faces = facing is "toward camera" or "three-quarter left" or "three-quarter right";
        var backs = facing is "away from camera" or "back three-quarter left" or "back three-quarter right";
        if (faces && moving.StartsWith("away from camera", StringComparison.Ordinal))
        {
            return $"faces the camera ({facing}) while going away from it";
        }

        if (backs && moving.StartsWith("toward camera", StringComparison.Ordinal))
        {
            return $"has their back to the camera ({facing}) while coming toward it";
        }

        return Horizontal(moving) switch
        {
            LeftToRight when facing.EndsWith("left", StringComparison.Ordinal) => $"faces {facing} while moving {LeftToRight}",
            RightToLeft when facing.EndsWith("right", StringComparison.Ordinal) => $"faces {facing} while moving {RightToLeft}",
            _ => null,
        };
    }

    public static string Moving(string? answer)
    {
        var text = Normal(answer).Replace("towards", "toward").Replace("the camera", "camera").Replace("screen ", "").Replace(" and ", ", ");
        return text switch
        {
            "none" or "stationary" or "standing" or "not moving" => "still",
            _ => Match(DirectionVocabulary.Movings, text),
        };
    }

    public static string Travel(string? answer) => Match(DirectionVocabulary.Travels, Normal(answer).Replace("screen ", ""));

    /// <summary>The across-the-screen part of a <see cref="DirectionVocabulary.Movings"/> label, if any.</summary>
    public static string? Horizontal(string moving) =>
        moving.EndsWith(LeftToRight, StringComparison.Ordinal) ? LeftToRight
        : moving.EndsWith(RightToLeft, StringComparison.Ordinal) ? RightToLeft
        : null;

    /// <summary>Left before centre before right; null when off screen or unplaced.</summary>
    public static int? Order(string screen) => screen switch
    {
        "left" => 0,
        "centre" => 1,
        "right" => 2,
        _ => null,
    };

    private static string Match(IReadOnlyList<string> labels, string text) =>
        labels.FirstOrDefault(l => Normal(l) == text) ?? "";

    private static string Normal(string? text) =>
        Spaces().Replace((text ?? "").ToLowerInvariant().Replace('-', ' ').Replace('_', ' ').Replace("→", " to "), " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
