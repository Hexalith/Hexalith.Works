using Hexalith.EventStore.DomainService;

namespace Hexalith.Works.Recovery;

/// <summary>Maps protected disposition to the SDK authorization, durable audit and revalidation pipeline.</summary>
internal static class WorksProcessDispositionEndpoints
{
    internal static void MapWorksProcessDisposition(this WebApplication app)
        => app.MapPost("/work/processes/revalidate", static async (
            WorksProcessDispositionRequest request, HttpContext context, ProcessDispositionService disposition,
            CancellationToken cancellationToken) =>
        {
            if (request.Source is null || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 256)
                return Results.BadRequest();
            try
            {
                bool completed = await disposition.RevalidateAsync(context.User, request.Source, request.Reason, cancellationToken).ConfigureAwait(false);
                return completed ? Results.NoContent() : Results.Accepted();
            }
            catch (UnauthorizedAccessException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (ArgumentException) { return Results.BadRequest(); }
        }).RequireEventStoreDomainServicePolicy("/process");
}
