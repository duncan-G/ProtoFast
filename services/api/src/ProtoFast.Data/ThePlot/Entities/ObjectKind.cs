namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>Decides how long an object's facts last: a fixture until a beat changes it, an occupant until its scene ends.</summary>
public enum ObjectKind
{
    Fixture,
    Prop,
    Dressing,
    Occupant,
}
