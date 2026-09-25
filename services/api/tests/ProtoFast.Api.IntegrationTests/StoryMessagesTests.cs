using ProtoFast.Api.Services.Screenplays;
using Xunit;
using AvatarShapeRecord = ProtoFast.Data.ThePlot.Entities.AvatarShape;

namespace ProtoFast.Api.IntegrationTests;

public class StoryMessagesTests
{
    [Fact]
    public void Every_avatar_shape_maps_to_the_proto_member_of_the_same_name_and_back()
    {
        foreach (var shape in Enum.GetValues<AvatarShapeRecord>())
        {
            var message = StoryMessages.ToMessage(shape);
            Assert.Equal(shape.ToString(), message.ToString());
            Assert.Equal(shape, StoryMessages.FromMessage(message));
        }

        Assert.Equal(Enum.GetValues<AvatarShapeRecord>().Length, Enum.GetValues<AvatarShape>().Length - 1);
    }
}
