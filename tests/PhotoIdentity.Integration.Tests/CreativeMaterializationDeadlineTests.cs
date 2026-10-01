using Microsoft.AspNetCore.Http;
using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CreativeMaterializationDeadlineTests
{
    [Fact]
    public async Task Server_deadline_cancels_work_and_returns_service_unavailable()
    {
        IResult result = await CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(
            async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Results.Ok();
            }, CancellationToken.None, TimeSpan.FromMilliseconds(10));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_server_timeout()
    {
        using CancellationTokenSource caller = new();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(
                token => { token.ThrowIfCancellationRequested(); return Task.FromResult(Results.Ok()); },
                caller.Token));
    }

    [Fact]
    public async Task Unrelated_cancellation_is_not_mislabelled_and_success_is_preserved()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(
                _ => throw new OperationCanceledException(), CancellationToken.None));
        IResult expected = Results.NotFound();
        Assert.Same(expected, await CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(
            _ => Task.FromResult(expected), CancellationToken.None));
    }
}
