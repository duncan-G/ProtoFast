using ProtoFast.DocumentImport.Worker.WorldModel;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.WorldModel;

public sealed class SceneReaderTests
{
    [Fact]
    public void A_reply_is_read_past_its_prose_and_fence()
    {
        var reading = SceneReader.Parse("""
            Here is the reading:
            ```json
            {
              "parts": [{"ref": "p1", "name": "hallway: start", "position": 0}],
              "objects": [{"ref": "o1", "name": "double doors", "aliases": ["the doors"], "kind": "fixture"}],
              "aliases": [],
              "edges": [
                {"beatId": "b1", "edge": "at", "objectId": "o1", "partId": "p1", "basis": "stated"},
                {"beatId": "b1", "edge": "next", "partId": "p1", "nextPartId": "p2"},
              ]
            }
            ```
            """);

        Assert.Equal("hallway: start", Assert.Single(reading.Parts).Name);
        Assert.Equal(["the doors"], Assert.Single(reading.Objects).Aliases);
        Assert.Equal(2, reading.Edges.Count);
        Assert.Equal(("at", "o1", "p1"), (reading.Edges[0].Edge, reading.Edges[0].ObjectId, reading.Edges[0].PartId));
    }

    [Fact]
    public void A_reply_by_beat_reads_as_edges_each_labelled_with_its_beat()
    {
        var reading = SceneReader.Parse("""
            {
              "parts": [], "objects": [], "aliases": [],
              "inside": [{"locationId": "L2", "insideLocationId": "L1"}],
              "next": [{"partId": "p1", "nextPartId": "p2"}],
              "beats": [
                {"beatId": "b1", "action": {"partId": "p1", "basis": "stated"},
                 "camera": {"partId": "p2", "towardPartId": "p1", "basis": "implied"},
                 "characters": [{"characterId": "C1", "partId": "p1", "basis": "stated"}],
                 "edges": [{"edge": "at", "objectId": "o1", "partId": "p1", "basis": "stated"}]},
                {"beatId": "b2", "camera": {"partId": "p2"}}
              ]
            }
            """);

        Assert.Equal(
            [("inside", null), ("next", null), ("beat_at", "b1"), ("camera", "b1"), ("character_at", "b1"), ("at", "b1"), ("camera", "b2")],
            reading.AllEdges().Select(e => (e.Edge, e.BeatId)));
        Assert.Equal("p1", reading.AllEdges().Single(e => e.Edge == "camera" && e.BeatId == "b1").TowardPartId);
    }

    [Fact]
    public void A_reply_with_no_object_in_it_is_refused()
    {
        Assert.ThrowsAny<Exception>(() => SceneReader.Parse("I could not read this scene."));
    }
}
