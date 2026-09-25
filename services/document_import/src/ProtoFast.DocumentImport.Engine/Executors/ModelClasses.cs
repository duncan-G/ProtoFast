namespace ProtoFast.DocumentImport.Engine;

public static class ModelClasses
{
    public const string Large = "large";
    public const string Medium = "medium";
    public const string Small = "small";

    public static string? For(Tier tier) => tier switch
    {
        Tier.DelegateLarge => Large,
        Tier.DelegateMedium => Medium,
        Tier.DelegateSmall => Small,
        _ => null,
    };
}
