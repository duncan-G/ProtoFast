using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class ViewPictureQuery : Query<ViewPicture>, IViewPictureQuery
{
    public IViewPictureQuery WithId(Guid id)
    {
        Where(p => p.Id == id);
        return this;
    }

    public IViewPictureQuery InStory(Guid storyId)
    {
        Where(p => p.StoryId == storyId);
        return this;
    }

    public IViewPictureQuery Of(Guid sceneId, Guid cameraLocationId, string? cameraPart, Guid? towardLocationId, string? towardPart, int state)
    {
        Where(p => p.SceneId == sceneId
                   && p.CameraLocationId == cameraLocationId
                   && p.CameraPart == cameraPart
                   && p.TowardLocationId == towardLocationId
                   && p.TowardPart == towardPart
                   && p.State == state);
        return this;
    }
}
