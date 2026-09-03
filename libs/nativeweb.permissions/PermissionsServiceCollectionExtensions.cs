using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NativeWeb.Permissions;

/// <summary>Wiring for the permission layer.</summary>
public static class PermissionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the permission decision service, the per-request context, and the
    /// registry of everything this application declares.
    /// </summary>
    /// <remarks>
    /// <para>The store is <b>not</b> registered here. A host chooses it: the
    /// in-memory one through <see cref="AddInMemoryPermissionStore"/>, or its own
    /// over whatever it keeps its data in. Registering a default would mean an
    /// application that forgot its store silently ran on one that forgets every
    /// grant at restart.</para>
    /// <code>
    /// builder.Services.AddPermissions(PermissionCatalog.All);
    /// builder.Services.AddSingleton&lt;IPermissionStore, ArticlePermissionStore&gt;();
    /// </code>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="catalog">
    /// Everything this application declares, normally the generated
    /// <c>PermissionCatalog.All</c>. Passed rather than discovered, for the reason
    /// <see cref="PermissionRegistry"/> gives.
    /// </param>
    public static IServiceCollection AddPermissions(
        this IServiceCollection services, IEnumerable<Permission> catalog)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(catalog);

        services.TryAddSingleton(new PermissionRegistry(catalog));
        services.TryAddScoped<IPermissionContext, PermissionContext>();
        services.TryAddScoped<IPermissions, PermissionService>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryPermissionStore"/> as the store. For tests, and
    /// for a host that wants the access admin working before it has written a real
    /// one. Nothing survives a restart.
    /// </summary>
    public static IServiceCollection AddInMemoryPermissionStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPermissionStore>(new InMemoryPermissionStore());
        return services;
    }
}
