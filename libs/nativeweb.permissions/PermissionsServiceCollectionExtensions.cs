using System.Security.Claims;

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
        this IServiceCollection services, IEnumerable<Permission> catalog) =>
        services.AddPermissions(catalog, null);

    /// <summary>
    /// The same, naming the claim -- or the function -- that says which subject a
    /// principal is.
    /// </summary>
    /// <remarks>
    /// <para>Configuring nothing is <see cref="ClaimTypes.NameIdentifier"/>, which is
    /// what every application on this layer had before the option existed, so this
    /// overload changes nothing for a host that does not call it.</para>
    /// <code>
    /// // a scheme that leaves `sub` alone
    /// builder.Services.AddPermissions(PermissionCatalog.All, options =&gt; options.ClaimName = "sub");
    ///
    /// // a subject that is not one claim
    /// builder.Services.AddPermissions(
    ///     PermissionCatalog.All,
    ///     options =&gt; options.Resolve = user =&gt; Compose(user));
    /// </code>
    /// <para>The delegate runs here, not on the first request, so configuring both a
    /// claim name and a function stops the application at startup rather than
    /// refusing every gated route once it is serving.</para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="catalog">Everything this application declares.</param>
    /// <param name="configure">Sets the claim name, or the resolver, or neither.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="configure"/> set both a claim name and a resolver.
    /// </exception>
    public static IServiceCollection AddPermissions(
        this IServiceCollection services,
        IEnumerable<Permission> catalog,
        Action<PermissionSubjectOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(catalog);

        var options = new PermissionSubjectOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(new PermissionRegistry(catalog));
        services.TryAddSingleton(new PermissionSubjectResolver(options));
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
