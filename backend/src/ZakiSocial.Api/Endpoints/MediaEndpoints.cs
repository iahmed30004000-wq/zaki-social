using ZakiSocial.Infrastructure.Auth;
using ZakiSocial.Infrastructure.Media;

namespace ZakiSocial.Api.Endpoints;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/media");

        g.MapGet("", async (MediaService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)))
            .RequireAuthorization(AuthPolicies.Member);

        g.MapGet("/{id:guid}", async (Guid id, MediaService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(id, ct)))
            .RequireAuthorization(AuthPolicies.Member);

        // رفع: multipart (حقل "file"). المعالجة بالخلفية، والرد بيرجع بحالة processing.
        g.MapPost("", async (IFormFile file, MediaService svc, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return Results.Ok(await svc.UploadAsync(stream, file.FileName, file.ContentType, file.Length, ct));
        }).RequireAuthorization(AuthPolicies.Editor).DisableAntiforgery();

        g.MapGet("/{id:guid}/file", async (Guid id, MediaService svc, CancellationToken ct) =>
        {
            var (stream, type) = await svc.OpenAsync(id, "file", ct);
            return Results.Stream(stream, type, enableRangeProcessing: false);
        }).RequireAuthorization(AuthPolicies.Member);

        g.MapGet("/{id:guid}/thumb", async (Guid id, MediaService svc, CancellationToken ct) =>
        {
            var (stream, type) = await svc.OpenAsync(id, "thumb", ct);
            return Results.Stream(stream, type);
        }).RequireAuthorization(AuthPolicies.Member);

        g.MapPost("/{id:guid}/approve", async (Guid id, MediaService svc, CancellationToken ct) => Results.Ok(await svc.ApproveAsync(id, ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        g.MapDelete("/{id:guid}", async (Guid id, MediaService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Editor);
    }
}
