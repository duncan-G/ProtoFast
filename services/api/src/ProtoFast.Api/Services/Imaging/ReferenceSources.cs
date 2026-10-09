namespace ProtoFast.Api.Services.Imaging;

/// <summary><see cref="ReferenceSource"/> as stored on a <c>MediaReference</c> and sent to the client.</summary>
public static class ReferenceSources
{
    public const string Character = "character";
    public const string Location = "location";
    public const string Prop = "prop";
    public const string Frame = "frame";

    public static string Of(ReferenceSource source) => source switch
    {
        ReferenceSource.Character => Character,
        ReferenceSource.Location => Location,
        ReferenceSource.Prop => Prop,
        _ => Frame,
    };
}
