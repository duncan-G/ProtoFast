using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.DocumentImport.Worker.WorldModel;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.WorldModel;

public sealed class LocationNamesTests
{
    private static readonly Location Hospital = new() { Id = Guid.NewGuid(), Name = "HOSPITAL" };
    private static readonly Location Lobby = new() { Id = Guid.NewGuid(), Name = "LOBBY" };
    private static readonly Location Wing = new() { Id = Guid.NewGuid(), Name = "TRAUMA WING" };
    private static readonly Location Hallway = new() { Id = Guid.NewGuid(), Name = "HOSPITAL / HALLWAY", InsideLocationId = Hospital.Id };
    private static readonly Location[] Locations = [Hospital, Lobby, Wing, Hallway];

    [Theory]
    [InlineData("HOSPITAL – TRAUMA WING", "HOSPITAL", "TRAUMA WING")]
    [InlineData("HOSPITAL / 4TH FLOOR - ICU", "HOSPITAL / 4TH FLOOR", "ICU")]
    [InlineData("FARM — GATE", "FARM", "GATE")]
    public void A_nesting_name_splits_at_its_last_separator(string name, string outer, string inner) =>
        Assert.Equal((outer, inner), LocationNames.Split(name));

    [Fact]
    public void A_name_that_does_not_nest_does_not_split() => Assert.Null(LocationNames.Split("BRYAN'S CAR"));

    [Theory]
    [InlineData("lobby")]
    [InlineData("The Lobby")]
    [InlineData("  LOBBY ")]
    public void A_part_named_like_another_location_is_that_location(string part) =>
        Assert.Equal(Lobby.Id, LocationNames.Shadowed(part, Hospital.Id, Locations));

    [Fact]
    public void A_part_named_like_a_location_nested_in_its_own_is_that_location() =>
        Assert.Equal(Hallway.Id, LocationNames.Shadowed("hallway", Hospital.Id, Locations));

    [Fact]
    public void A_hallway_elsewhere_is_only_a_part() => Assert.Null(LocationNames.Shadowed("hallway", Wing.Id, Locations));

    [Theory]
    [InlineData("lobby: counter")]
    [InlineData("the")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_names_are_parts(string? part) => Assert.Null(LocationNames.Shadowed(part, Hospital.Id, Locations));

    [Fact]
    public void A_location_is_never_a_part_of_itself() => Assert.Null(LocationNames.Shadowed("lobby", Lobby.Id, Locations));

    [Fact]
    public void A_part_named_after_another_location_is_a_part_of_it() =>
        Assert.Equal((Lobby.Id, "counter"), LocationNames.Prefixed("lobby: counter", Hospital.Id, Locations));

    [Fact]
    public void A_part_named_after_its_own_location_drops_the_name() =>
        Assert.Equal((Wing.Id, "nurses' station"), LocationNames.Prefixed("Trauma wing: nurses' station", Wing.Id, Locations));

    [Theory]
    [InlineData("hallway: start")]
    [InlineData("lobby:")]
    [InlineData("counter")]
    [InlineData(null)]
    public void A_position_of_a_space_is_not_prefixed(string? part) => Assert.Null(LocationNames.Prefixed(part, Wing.Id, Locations));
}
