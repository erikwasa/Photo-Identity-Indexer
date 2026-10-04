using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using PhotoIdentity.Web.Contracts;
using PhotoIdentity.Web.Pages;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class AssignmentAuditPagingTests
{
    [Fact]
    public async Task Overlapping_pages_are_deduplicated_and_server_offset_still_advances()
    {
        int request = 0;
        using HttpClient http = new(new Handler(_ =>
        {
            request++;
            return request switch
            {
                1 => Page([Face("a"), Face("b")], offset: 0, total: 4),
                2 => Page([Face("b"), Face("c")], offset: 2, total: 4),
                _ => throw new InvalidOperationException("Unexpected audit page request."),
            };
        }))
        {
            BaseAddress = new Uri("http://example.test"),
        };

        PersonAudit component = new();
        SetProperty(component, "Http", http);

        Assert.True(await InvokeLoadNextPageAsync(component));
        Assert.True(await InvokeLoadNextPageAsync(component));

        List<AssignmentAuditFaceResponse> items = GetProperty<List<AssignmentAuditFaceResponse>>(component, "Items");
        Assert.Equal(["a", "b", "c"], items.Select(item => item.Id));
        Assert.Equal(4, GetProperty<int>(component, "ServerOffset"));
        Assert.False(GetProperty<bool>(component, "HasMore"));
    }

    [Fact]
    public async Task Fully_overlapping_page_cannot_leave_lazy_loading_stuck_on_the_same_offset()
    {
        int request = 0;
        using HttpClient http = new(new Handler(_ =>
        {
            request++;
            return request switch
            {
                1 => Page([Face("a"), Face("b")], offset: 0, total: 4),
                2 => Page([Face("a"), Face("b")], offset: 2, total: 4),
                _ => throw new InvalidOperationException("Unexpected audit page request."),
            };
        }))
        {
            BaseAddress = new Uri("http://example.test"),
        };

        PersonAudit component = new();
        SetProperty(component, "Http", http);

        Assert.True(await InvokeLoadNextPageAsync(component));
        Assert.True(await InvokeLoadNextPageAsync(component));

        List<AssignmentAuditFaceResponse> items = GetProperty<List<AssignmentAuditFaceResponse>>(component, "Items");
        Assert.Equal(["a", "b"], items.Select(item => item.Id));
        Assert.Equal(4, GetProperty<int>(component, "ServerOffset"));
        Assert.False(GetProperty<bool>(component, "HasMore"));
    }

    private static Task<bool> InvokeLoadNextPageAsync(PersonAudit component) =>
        (Task<bool>)typeof(PersonAudit)
            .GetMethod("LoadNextPageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, null)!;

    private static T GetProperty<T>(PersonAudit component, string name) =>
        (T)typeof(PersonAudit)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(component)!;

    private static void SetProperty(PersonAudit component, string name, object value) =>
        typeof(PersonAudit)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(component, value);

    private static HttpResponseMessage Page(
        IReadOnlyList<AssignmentAuditFaceResponse> items,
        int offset,
        int total) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AssignmentAuditPageResponse(
                items,
                offset,
                2,
                total,
                "automatic",
                null,
                null)),
        };

    private static AssignmentAuditFaceResponse Face(string id) =>
        new(
            id,
            $"api/review/faces/{id}/crop",
            $"{id}.jpg",
            0,
            0.99,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            1,
            "identity-matcher:auto",
            new ReviewPersonResponse("person", "Example"),
            null,
            null,
            false);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
