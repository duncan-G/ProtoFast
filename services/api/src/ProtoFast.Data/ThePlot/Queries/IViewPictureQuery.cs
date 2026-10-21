using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="ViewPicture"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IViewPictureRepository"/>.
/// </summary>
public interface IViewPictureQuery : IQuery<ViewPicture>
{
    IViewPictureQuery WithId(Guid id);

    IViewPictureQuery InStory(Guid storyId);

    IViewPictureQuery Of(Guid sceneId, Guid cameraLocationId, string? cameraPart, Guid? towardLocationId, string? towardPart, int state);
}
