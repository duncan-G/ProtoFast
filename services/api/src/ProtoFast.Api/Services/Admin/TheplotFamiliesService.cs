using Grpc.Core;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.DocumentImport.Engine.Families;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.Grpc;

namespace ProtoFast.Api.Services.Admin;

/// <summary>Document families for theplot's console, with the two things an operator may change.</summary>
public sealed class TheplotFamiliesService(
    IDocumentFamilyDirectory families,
    IDocumentFamilyRegistry registry,
    IDocumentFamilyGenerations generations,
    TimeProvider time) : TheplotFamilies.TheplotFamiliesBase
{
    public const string App = TheplotAdminService.App;

    private const int MaxDisplayName = 200;
    private const string FamilyNotFound = "Document family not found.";

    public override async Task<ListFamiliesReply> ListFamilies(ListFamiliesRequest request, ServerCallContext context)
    {
        AdminAccess.Require(context, App);

        var reply = new ListFamiliesReply();
        reply.Families.AddRange((await families.ListAsync(context.CancellationToken)).Select(EngineMessages.From));
        return reply;
    }

    public override async Task<GetFamilyReply> GetFamily(GetFamilyRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, FamilyNotFound);

        var detail = await families.FindAsync(
                         request.Family, request.HasGeneration ? Math.Max(request.Generation, 0) : null, context.CancellationToken)
                     ?? throw new RpcException(new Status(StatusCode.NotFound, FamilyNotFound));
        return EngineMessages.From(detail);
    }

    public override async Task<CreateFamilyReply> CreateFamily(CreateFamilyRequest request, ServerCallContext context)
    {
        var caller = AdminAccess.Require(context, App);
        var (displayName, description) = Validate(request.Family, request.DisplayName, request.Description);

        var now = time.GetUtcNow();
        try
        {
            await registry.CreateAsync(
                new DocumentFamilyInfo(request.Family, displayName, description, caller.Subject, now, now),
                context.CancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "A document family with that name already exists."));
        }

        return new CreateFamilyReply();
    }

    public override async Task<UpdateFamilyReply> UpdateFamily(UpdateFamilyRequest request, ServerCallContext context)
    {
        var caller = AdminAccess.RequireRow(context, App, FamilyNotFound);
        var (displayName, description) = Validate(request.Family, request.DisplayName, request.Description);

        var existing = await families.FindAsync(request.Family, null, context.CancellationToken)
                       ?? throw new RpcException(new Status(StatusCode.NotFound, FamilyNotFound));
        var now = time.GetUtcNow();
        await registry.UpdateAsync(
            new DocumentFamilyInfo(
                request.Family,
                displayName,
                description,
                existing.Info?.CreatedBy ?? caller.Subject,
                existing.Info?.CreatedAt ?? now,
                now),
            context.CancellationToken);
        return new UpdateFamilyReply();
    }

    public override async Task<ResetFamilyReply> ResetFamily(ResetFamilyRequest request, ServerCallContext context)
    {
        AdminAccess.RequireRow(context, App, FamilyNotFound);

        if (!DocumentFamilyNames.IsValid(request.Family)
            || await families.FindAsync(request.Family, null, context.CancellationToken) is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, FamilyNotFound));
        }

        return new ResetFamilyReply { Generation = await generations.ResetAsync(request.Family, context.CancellationToken) };
    }

    private static (string DisplayName, string Description) Validate(string family, string displayName, string description)
    {
        if (!DocumentFamilyNames.IsValid(family))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"A family name is lowercase letters, digits, '-' and '_', starts with a letter and is at most {DocumentFamilyNames.MaxLength} characters."));
        }

        var name = displayName.Trim();
        if (name.Length is 0 or > MaxDisplayName)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"A display name is 1 to {MaxDisplayName} characters."));
        }

        return (name, description.Trim());
    }
}
