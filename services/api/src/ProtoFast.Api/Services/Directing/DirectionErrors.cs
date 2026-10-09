namespace ProtoFast.Api.Services.Directing;

public static class DirectionErrors
{
    public const string PanelNotFound = "That panel is no longer in the scene.";
    public const string LineNotFound = "That line is no longer in the scene.";
    public const string ModelUnavailable = "The language model isn’t available right now. Try again in a moment.";
    public const string ModelUnusable = "The model’s answer couldn’t be used. Try again.";
}
