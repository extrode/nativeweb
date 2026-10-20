using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using NativeWeb.Sample;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

public sealed class ScaffoldedRefusalTests
{
    private const string Message = "Slug 'aot' is already used.";

    private sealed class RefusingTagStore : IScaffoldStore<Tag>
    {
        private static readonly Tag Row = new() { Slug = "aot", Name = "NativeAOT", Color = "#5bbd83" };

        public ValueTask<IReadOnlyList<Tag>> ListAsync(
            string? search,
            IReadOnlyList<string> searchFields,
            ScaffoldSortSpec? sort,
            IReadOnlyList<string> sortFields,
            ScaffoldCursor? after,
            int take,
            CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<Tag>>([Row]);

        public ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct) =>
            ValueTask.FromResult(1);

        public ValueTask<Tag?> GetAsync(string id, CancellationToken ct) =>
            ValueTask.FromResult<Tag?>(id == Row.Slug ? Row : null);

        public ValueTask<string> CreateAsync(Tag item, CancellationToken ct) =>
            throw new ScaffoldRefusedException(Message);

        public ValueTask<bool> UpdateAsync(string id, Tag item, CancellationToken ct) =>
            throw new ScaffoldRefusedException(Message);

        public ValueTask<bool> DeleteAsync(string id, CancellationToken ct) =>
            ValueTask.FromResult(false);
    }

    private sealed class BrokenTagStore : IScaffoldStore<Tag>
    {
        public ValueTask<IReadOnlyList<Tag>> ListAsync(
            string? search,
            IReadOnlyList<string> searchFields,
            ScaffoldSortSpec? sort,
            IReadOnlyList<string> sortFields,
            ScaffoldCursor? after,
            int take,
            CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<Tag>>([]);

        public ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct) =>
            ValueTask.FromResult(0);

        public ValueTask<Tag?> GetAsync(string id, CancellationToken ct) =>
            ValueTask.FromResult<Tag?>(null);

        public ValueTask<string> CreateAsync(Tag item, CancellationToken ct) =>
            throw new InvalidOperationException("the connection is gone");

        public ValueTask<bool> UpdateAsync(string id, Tag item, CancellationToken ct) =>
            throw new InvalidOperationException("the connection is gone");

        public ValueTask<bool> DeleteAsync(string id, CancellationToken ct) =>
            ValueTask.FromResult(false);
    }

    private static HttpClient ClientOver(SampleFactory factory, IScaffoldStore<Tag> store) =>
        factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddSingleton(store)))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Create_ThatTheStoreRefuses_ShowsTheFormAgainWithTheMessageAndTheValues()
    {
        using var factory = new SampleFactory();
        using HttpClient client = ClientOver(factory, new RefusingTagStore());

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/tags/new",
            "/admin/tags",
            new("name", "Native AOT again"),
            new("color", "#123456"));

        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<p class=\"nws-error\" role=\"alert\">" + Message.Replace("'", "&#x27;", StringComparison.Ordinal), body, StringComparison.Ordinal);
        Assert.Contains("value=\"Native AOT again\"", body, StringComparison.Ordinal);
        Assert.Contains("value=\"#123456\"", body, StringComparison.Ordinal);
        Assert.Contains("<h1>New Tag</h1>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_ThatTheStoreRefuses_ShowsTheEditFormAgainWithTheMessageAndTheValues()
    {
        using var factory = new SampleFactory();
        using HttpClient client = ClientOver(factory, new RefusingTagStore());

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/tags/aot",
            "/admin/tags/aot",
            new("name", "Renamed"),
            new("color", "#ffffff"));

        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<p class=\"nws-error\" role=\"alert\">" + Message.Replace("'", "&#x27;", StringComparison.Ordinal), body, StringComparison.Ordinal);
        Assert.Contains("value=\"Renamed\"", body, StringComparison.Ordinal);
        Assert.Contains("<h1>Edit Tag</h1>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnyOtherExceptionFromTheStore_IsStillAnError()
    {
        using var factory = new SampleFactory();
        using HttpClient client = ClientOver(factory, new BrokenTagStore());

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/tags/new",
            "/admin/tags",
            new("name", "Anything"),
            new("color", ""));

        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("nws-error", body, StringComparison.Ordinal);
        Assert.DoesNotContain("the connection is gone", body, StringComparison.Ordinal);
    }
}
