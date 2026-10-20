using Microsoft.Extensions.DependencyInjection;

namespace NativeWeb.Scaffold;

/// <summary>
/// Service registration for scaffolding.
/// </summary>
public static class ScaffoldServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services every scaffolded route needs: antiforgery, because
    /// each generated form emits a token and each mutating route carries
    /// <see cref="RequiresAntiforgery"/>; and authorization, because
    /// <c>[Auth]</c> emits <c>RequireAuthorization</c>.
    /// </summary>
    /// <remarks>
    /// <para>This is two framework calls, not a policy. It deliberately does not
    /// register an authentication scheme: cookie name, login path and role
    /// membership are the application's security posture, and a default supplied
    /// from a library is one nobody reviews. A host that calls this and no
    /// <c>AddAuthentication</c> gets scaffolded routes that 401, which is the
    /// correct failure.</para>
    /// <para>Equivalent to writing both calls by hand. Nothing here is load-bearing
    /// beyond saving the two lines, so replacing it is a supported thing to do.</para>
    /// </remarks>
    public static IServiceCollection AddScaffolding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAntiforgery();
        services.AddAuthorization();

        return services;
    }
}
