namespace ProtoFast.Api.Services.Imaging;

public enum ReferenceSource
{
    Character,
    Location,
    Prop,
    Frame,

    /// <summary>A character's picture in one of their looks.</summary>
    Look,

    Extra,

    /// <summary>A part of a location, pictured from its establishing shot.</summary>
    LocationVariation,
}
