using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NativeWeb.SignIn;

/// <summary>Wiring for OAuth sign-in.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a sign-in provider under the default scheme name.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Fills in <see cref="SignInOptions"/>.</param>
    /// <returns>The builder, for the optional seams.</returns>
    public static SignInBuilder AddSignIn(
        this IServiceCollection services,
        Action<SignInOptions> configure) =>
        services.AddSignIn(SessionAuthenticationHandler.SchemeName, configure);

    /// <summary>
    /// Registers a sign-in provider under <paramref name="scheme"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="scheme">The authentication scheme name. Also the DI key every service this
    /// provider owns is registered under, so a host can configure two providers side by side.</param>
    /// <param name="configure">Fills in <see cref="SignInOptions"/>.</param>
    /// <returns>The builder, for the optional seams.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or
    /// <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="scheme"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The options are misconfigured — see
    /// <see cref="SignInOptions.Validate"/> — or this scheme is already registered.</exception>
    /// <remarks>
    /// Configuration is read MANUALLY: the consumer's delegate is the only way in. There is no
    /// <c>Bind()</c>, no configuration-binder source generator and no reflecting
    /// <c>IConfiguration</c> extension, because this library publishes AOT-clean and a reflecting
    /// binder is the one dependency that would stop it.
    /// <para>
    /// <see cref="SignInOptions.Validate"/> runs HERE rather than behind <c>IOptions&lt;T&gt;</c>, so
    /// a cookie name without the <c>__Host-</c> prefix fails at startup and not at the first request
    /// that happens to sign someone in.
    /// </para>
    /// <para>
    /// Everything this provider owns — its options, client, stores, session manager and
    /// introspection cache — is registered KEYED on the scheme name. Two calls therefore configure
    /// two independent providers rather than the second overwriting the first, and a session minted
    /// under one scheme is a value the other scheme's store has never heard of.
    /// </para>
    /// </remarks>
    public static SignInBuilder AddSignIn(
        this IServiceCollection services,
        string scheme,
        Action<SignInOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SignInOptions();
        configure(options);
        options.Validate();

        RefuseADuplicateScheme(services, scheme);

        // Read BEFORE this scheme's own options are registered: the check is "has any scheme been
        // registered", and the registration below would make every call answer no.
        bool isTheFirstScheme = IsTheFirstScheme(services);

        services.AddKeyedSingleton(scheme, options);

        // A timeout, because HttpClient's default is 100 seconds and the provider sits on the
        // synchronous path of a callback. A provider that has stopped answering should degrade to
        // "provider_unavailable" in seconds, not hold a request thread and a customer's browser for
        // a minute and a half first.
        services.AddHttpClient(scheme, static http => http.Timeout = ProviderTimeout);

        services.AddKeyedScoped(scheme, (sp, _) => new OAuthClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(scheme),
            sp.GetRequiredKeyedService<SignInOptions>(scheme),
            sp.GetRequiredService<ILogger<OAuthClient>>()));

        // The defaults are registered FIRST and plainly, not with TryAdd. The builder's
        // WithFlowStore/WithSessionStore append, and the last keyed registration is the one that
        // resolves — so a consumer's store wins without either registration having to know about the
        // other. TryAdd would invert that and hand the consumer a silent no-op.
        //
        // Both defaults are singletons behind a scoped facade: an in-memory store that was itself
        // scoped would lose every session and every in-flight flow at the end of the request that
        // created it.
        services.AddKeyedSingleton(scheme, (sp, _) => new InMemoryLoginFlowStore(
            sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedScoped<ILoginFlowStore>(
            scheme, (sp, _) => sp.GetRequiredKeyedService<InMemoryLoginFlowStore>(scheme));

        services.AddKeyedSingleton(scheme, (sp, _) => new InMemorySessionStore(
            sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedScoped<ISessionStore>(
            scheme, (sp, _) => sp.GetRequiredKeyedService<InMemorySessionStore>(scheme));

        services.AddKeyedScoped(scheme, (sp, _) => new SessionManager(
            sp.GetRequiredKeyedService<ISessionStore>(scheme),
            sp.GetRequiredKeyedService<OAuthClient>(scheme),
            sp.GetRequiredKeyedService<SignInOptions>(scheme),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<SessionManager>>()));

        services.AddKeyedSingleton(scheme, static (_, _) => new IntrospectionCache());

        // The sign-out endpoint validates an antiforgery token explicitly rather than relying on the
        // UseAntiforgery middleware, so IAntiforgery must resolve. TryAdd-shaped by AddAntiforgery
        // itself: a consumer who already called it keeps their options. Without this, a host that
        // never registered antiforgery boots clean and answers every sign-out with a 500.
        services.AddAntiforgery();

        // TryAdd: a consumer that already registered a TimeProvider — a fake one in tests, typically
        // — keeps it. Overwriting theirs would make the library's clock disagree with the
        // application's.
        services.TryAddSingleton(TimeProvider.System);

        // The README's wiring block ends with UseAuthentication/UseAuthorization, and
        // UseAuthorization throws at startup if nothing registered the authorization services.
        // Nothing in a slim host does. A consumer following the documented wiring verbatim should
        // not meet an exception naming a method the documentation never mentioned.
        services.AddAuthorization();

        AuthenticationBuilder authentication = isTheFirstScheme
            // The FIRST scheme registered is the host's default, so a scaffolded route's bare
            // RequireAuthorization() has something to challenge with. A second AddSignIn must not
            // move the default out from under it.
            ? services.AddAuthentication(scheme)
            : services.AddAuthentication();

        authentication.AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
            scheme,
            displayName: null,
            // The handler carries no options of its own — everything it reads lives on the keyed
            // SignInOptions it resolves for its scheme. An empty configure is the honest way to say
            // that; the two-argument overload does not exist.
            configureOptions: static _ => { });

        // Registered FIRST among this scheme's hosted services: hosted services start in
        // registration order, so a container that cannot build this scheme's services stops the host
        // before the sweeper ever looks for one.
        services.AddSingleton<IHostedService>(sp => new SignInStartupCheck(
            sp.GetRequiredService<IServiceScopeFactory>(), scheme));

        // Garbage collection for the flow table, the session table and the introspection cache. No
        // security property depends on it having run; see SignInSweeper.
        services.AddSingleton<IHostedService>(sp => new SignInSweeper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<SignInSweeper>>(),
            sp.GetRequiredService<TimeProvider>(),
            scheme));

        return new SignInBuilder(services, scheme);
    }

    /// <summary>How long to wait on the provider before calling it unavailable.</summary>
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether no scheme has been registered yet, judged by whether any keyed
    /// <see cref="SignInOptions"/> is present.
    /// </summary>
    private static bool IsTheFirstScheme(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(SignInOptions) && descriptor.IsKeyedService)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Refuses a second registration under one scheme name.
    /// </summary>
    /// <remarks>
    /// Without it the second call's options, stores and client all shadow the first's while the
    /// first's authentication scheme registration throws instead — a half-replaced provider whose
    /// error names the authentication stack rather than the duplicate call that caused it.
    /// </remarks>
    private static void RefuseADuplicateScheme(IServiceCollection services, string scheme)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(SignInOptions)
                && descriptor.IsKeyedService
                && string.Equals(descriptor.ServiceKey as string, scheme, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"A sign-in provider is already registered under the '{scheme}' scheme. Give the "
                    + "second provider a scheme name of its own.");
            }
        }
    }
}

/// <summary>
/// Fails startup when a scheme's services cannot be built.
/// </summary>
/// <remarks>
/// The container resolves lazily, so a registration that cannot be satisfied — a consumer store with
/// a dependency nothing supplies, say — would otherwise first be discovered by the request that
/// tried to sign someone in. This asks for each of them once, at startup, where the stack trace is
/// still about wiring.
/// </remarks>
/// <param name="scopes">The scope factory.</param>
/// <param name="scheme">The scheme to check.</param>
internal sealed class SignInStartupCheck(IServiceScopeFactory scopes, string scheme) : IHostedService
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<SignInOptions>(scheme);
        scope.ServiceProvider.GetRequiredKeyedService<OAuthClient>(scheme);
        scope.ServiceProvider.GetRequiredKeyedService<ILoginFlowStore>(scheme);
        scope.ServiceProvider.GetRequiredKeyedService<ISessionStore>(scheme);
        scope.ServiceProvider.GetRequiredKeyedService<SessionManager>(scheme);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
