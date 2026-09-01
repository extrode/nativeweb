using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using NativeWeb.Sample;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The generated update handler's second 404: the row was read and then vanished.
/// </summary>
/// <remarks>
/// <para>
/// <c>UpdateAsync</c> answers <see cref="HttpStatusCode.NotFound"/> twice for
/// different reasons. The first is an id that never resolved, which any store
/// produces. The second is <c>IScaffoldStore.UpdateAsync</c> returning false after
/// <c>GetAsync</c> already returned the row, which only a concurrent delete
/// produces and no in-memory store will do on demand.
/// </para>
/// <para>
/// Until 2026-08-31 that second branch was asserted from the emitted source and
/// never executed, so the emitter could have written the redirect there and every
/// suite stayed green. The store below is the concurrent delete, made
/// deterministic.
/// </para>
/// </remarks>
public sealed class ScaffoldedConcurrencyTests
{
    private sealed class VanishingTagStore : IScaffoldStore<Tag>
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
            ValueTask.FromResult(Row.Slug);

        public ValueTask<bool> UpdateAsync(string id, Tag item, CancellationToken ct) =>
            ValueTask.FromResult(false);

        public ValueTask<bool> DeleteAsync(string id, CancellationToken ct) =>
            ValueTask.FromResult(false);
    }

    private static HttpClient VanishingClient(SampleFactory factory) =>
        factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IScaffoldStore<Tag>>(new VanishingTagStore())))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Update_OfARowThatVanishedAfterItWasRead_IsNotFound()
    {
        using var factory = new SampleFactory();
        using HttpClient client = VanishingClient(factory);

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/tags/aot",
            "/admin/tags/aot",
            new("name", "Renamed"),
            new("color", "#ffffff"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_OfARowThatWasNeverThere_IsAlsoNotFound()
    {
        using var factory = new SampleFactory();
        using HttpClient client = VanishingClient(factory);

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/tags/aot",
            "/admin/tags/no-such-tag",
            new("name", "Renamed"),
            new("color", "#ffffff"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_AgainstTheRealStore_StillRedirects()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/tags/aot",
            "/admin/tags/aot",
            new("name", "Renamed"),
            new("color", "#ffffff"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/admin/tags", response.Headers.Location?.ToString());
    }
}
