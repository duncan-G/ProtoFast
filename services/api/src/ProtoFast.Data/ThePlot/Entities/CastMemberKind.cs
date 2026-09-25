namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// What a <see cref="CastMember"/> is; sets the avatar's shape in the editor.
/// Stored by name, so members can be reordered or added without a data migration; renaming one does need one.
/// </summary>
public enum CastMemberKind
{
    Human,
    Robot,
    Animal,
    Creature,
    Voice,
}
