using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot;

public static class ThePlotDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ThePlotDbContext"/> over the ambient <c>NpgsqlDataSource</c>
    /// (from <c>AddNpgsqlDataSource("protofast")</c>) together with the shared unit-of-work
    /// factory, and ThePlot's query factories and repositories.
    /// </summary>
    public static IServiceCollection AddThePlotData(
        this IServiceCollection services,
        Action<DatabaseOptions>? configureOptions = null)
    {
        services.AddCoreDatabaseServices<ThePlotDbContext>(configureOptions ?? (_ => { }));

        services.AddQueryFactory<Document, IDocumentQuery, DocumentQuery>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        services.AddQueryFactory<DocumentUpload, IDocumentUploadQuery, DocumentUploadQuery>();
        services.AddScoped<IDocumentUploadRepository, DocumentUploadRepository>();

        services.AddQueryFactory<Story, IStoryQuery, StoryQuery>();
        services.AddScoped<IStoryRepository, StoryRepository>();

        services.AddQueryFactory<Container, IContainerQuery, ContainerQuery>();
        services.AddScoped<IContainerRepository, ContainerRepository>();

        services.AddQueryFactory<Scene, ISceneQuery, SceneQuery>();
        services.AddScoped<ISceneRepository, SceneRepository>();

        services.AddQueryFactory<SceneElement, ISceneElementQuery, SceneElementQuery>();
        services.AddScoped<ISceneElementRepository, SceneElementRepository>();

        services.AddQueryFactory<Character, ICharacterQuery, CharacterQuery>();
        services.AddScoped<ICharacterRepository, CharacterRepository>();

        services.AddQueryFactory<Location, ILocationQuery, LocationQuery>();
        services.AddScoped<ILocationRepository, LocationRepository>();

        services.AddQueryFactory<Prop, IPropQuery, PropQuery>();
        services.AddScoped<IPropRepository, PropRepository>();

        services.AddQueryFactory<LocationPart, ILocationPartQuery, LocationPartQuery>();
        services.AddScoped<ILocationPartRepository, LocationPartRepository>();

        services.AddQueryFactory<LocationObject, ILocationObjectQuery, LocationObjectQuery>();
        services.AddScoped<ILocationObjectRepository, LocationObjectRepository>();

        services.AddQueryFactory<LocationObservation, ILocationObservationQuery, LocationObservationQuery>();
        services.AddScoped<ILocationObservationRepository, LocationObservationRepository>();

        services.AddQueryFactory<WorldModelBuild, IWorldModelBuildQuery, WorldModelBuildQuery>();
        services.AddScoped<IWorldModelBuildRepository, WorldModelBuildRepository>();

        services.AddQueryFactory<WorldModelBuildStep, IWorldModelBuildStepQuery, WorldModelBuildStepQuery>();
        services.AddScoped<IWorldModelBuildStepRepository, WorldModelBuildStepRepository>();

        services.AddQueryFactory<ViewPicture, IViewPictureQuery, ViewPictureQuery>();
        services.AddScoped<IViewPictureRepository, ViewPictureRepository>();

        return services;
    }
}
