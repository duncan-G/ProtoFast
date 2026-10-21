using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.WorldModel;
using ProtoFast.DocumentImport.Worker.WorldModel;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.WorldModel;

public sealed class LocationNestingTests
{
    [Fact]
    public void An_interior_opening_onto_a_nested_interior_joins_its_building()
    {
        var building = Location("HAPPY'S BUILDING", LocationSetting.Exterior);
        var apartment = Location("HAPPY'S APARTMENT", LocationSetting.Interior, building.Id);
        var hallway = Location("APARTMENT BUILDING HALLWAY", LocationSetting.Interior);
        var landing = Location("LANDING", LocationSetting.Interior);
        var street = Location("STREET", LocationSetting.Exterior);

        var nested = LocationNesting.Spread(
            [building, apartment, hallway, landing, street],
            [Connects(apartment.Id, hallway.Id), Connects(landing.Id, hallway.Id), Connects(street.Id, apartment.Id)]);

        Assert.Equal(2, nested);
        Assert.Equal(building.Id, hallway.InsideLocationId);
        Assert.Equal(building.Id, landing.InsideLocationId);
        Assert.Null(street.InsideLocationId);
    }

    [Fact]
    public void A_closed_way_or_a_rule_connection_nests_nothing()
    {
        var building = Location("BUILDING", LocationSetting.Exterior);
        var apartment = Location("APARTMENT", LocationSetting.Interior, building.Id);
        var hallway = Location("HALLWAY", LocationSetting.Interior);

        var nested = LocationNesting.Spread(
            [building, apartment, hallway],
            [Connects(apartment.Id, hallway.Id) with { Value = "closed" }, Connects(apartment.Id, hallway.Id) with { Origin = ObservationOrigin.Rule }]);

        Assert.Equal(0, nested);
        Assert.Null(hallway.InsideLocationId);
    }

    [Fact]
    public void A_room_entered_from_inside_a_vehicle_that_holds_rooms_is_one_of_its_rooms()
    {
        var ship = Location("SHIP", LocationSetting.Interior);
        var corridor = Location("CORRIDOR", LocationSetting.Interior, ship.Id);
        var cockpit = Location("COCKPIT", LocationSetting.Interior);
        var bar = Location("BAR", LocationSetting.Interior);

        var nested = LocationNesting.Spread([ship, corridor, cockpit, bar], [Connects(ship.Id, cockpit.Id)]);

        Assert.Equal(1, nested);
        Assert.Equal(ship.Id, cockpit.InsideLocationId);
        Assert.Null(bar.InsideLocationId);
        Assert.Null(ship.InsideLocationId);
    }

    private static Location Location(string name, LocationSetting setting, Guid? inside = null) =>
        new() { Id = Guid.NewGuid(), Name = name, Setting = setting, InsideLocationId = inside, UserId = "u" };

    private static ObservationRow Connects(Guid one, Guid other) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new StoryTime(0, 0, 0), ObservationEdge.Connects,
            null, null, one, null, other, null, null, null, null, ObservationBasis.Implied, ObservationOrigin.Extracted);
}
