using SonnetArt.Models;

namespace SonnetHost.StudioStorage;

public static class StudioSnapshotEndpoints
{
    public static void MapStudioSnapshotEndpoints(this WebApplication app)
    {
        app.MapGet("/api/studio/snapshot", async (
            HttpContext context,
            SonnetArtIdentityResolver identityResolver,
            StudioSnapshotStore store) =>
        {
            var identity = await identityResolver.ResolveAsync(context);
            if (identity is null)
            {
                return Results.Unauthorized();
            }

            var snapshot = await store.LoadAsync(identity, context.RequestAborted);
            return snapshot is null ? Results.NoContent() : Results.Json(snapshot);
        });

        app.MapPut("/api/studio/snapshot", async (
            HttpContext context,
            StudioSnapshot snapshot,
            SonnetArtIdentityResolver identityResolver,
            StudioSnapshotStore store) =>
        {
            var identity = await identityResolver.ResolveAsync(context);
            if (identity is null)
            {
                return Results.Unauthorized();
            }

            await store.SaveAsync(identity, snapshot, context.RequestAborted);
            return Results.NoContent();
        });

        app.MapDelete("/api/studio/snapshot", async (
            HttpContext context,
            SonnetArtIdentityResolver identityResolver,
            StudioSnapshotStore store) =>
        {
            var identity = await identityResolver.ResolveAsync(context);
            if (identity is null)
            {
                return Results.Unauthorized();
            }

            await store.DeleteAsync(identity, context.RequestAborted);
            return Results.NoContent();
        });

        app.MapDelete("/api/studio/auth-session", async (
            HttpContext context,
            SonnetArtIdentityResolver identityResolver) =>
        {
            await identityResolver.ClearAuthSessionAsync(context);
            return Results.NoContent();
        });
    }
}
