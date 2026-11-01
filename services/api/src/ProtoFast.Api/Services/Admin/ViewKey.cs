namespace ProtoFast.Api.Services.Admin;

/// <summary>A view as its picture is kept: parts by name, so the parts a rebuild makes anew still match.</summary>
internal readonly record struct ViewKey(Guid SceneId, Guid CameraLocationId, string? CameraPart, Guid? TowardLocationId, string? TowardPart, int State);
