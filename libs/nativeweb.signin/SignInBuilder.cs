using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NativeWeb.SignIn;

/// <summary>
/// Decides whether this application will begin a sign-in at all.
/// </summary>
/// <remarks>
/// Consulted at the sign-in endpoint, BEFORE a flow is recorded and before the browser is sent to
/// the provider, so a refusal costs no provider round trip and leaves no row behind (R22).
/// <para>
/// <b>It returns a boolean, and that is a security boundary rather than a simplification.</b> A
/// policy that could choose the failure code could choose a different one for a caller it
/// recognises, and sign-in would have become an account-enumeration oracle built out of a feature.
/// Every refusal here is reported as <see cref="SignInFailureCode.Refused"/> and nothing else, so no
/// two callers can be told apart by <i>the code</i> (R23).
/// </para>
/// <para>
/// <b>The rest of the response is the consumer's to keep identical, and the library cannot make it
/// so.</b> This delegate receives the live <see cref="HttpContext"/> — it must, or a policy could
/// not reach <c>RequestServices</c> or the client address — and a header, cookie or body written
/// there survives onto the refusal. So a policy must not vary the response by caller, and must not
/// vary the TIME it takes either.
/// </para>
/// <para>
/// A policy that throws is treated as a refusal and logged: consumer code on an unauthenticated path
/// fails closed. A cancellation the CLIENT caused is rethrown; one the policy caused is a broken
/// policy and refuses like any other throw.
/// </para>
/// <para>
/// The registered instance is a singleton and is called concurrently, so a policy must be thread-safe
/// and hold no per-request state of its own.
/// </para>
/// </remarks>
/// <param name="http">The sign-in request, for anything the consumer needs from it.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>True to begin the flow; false to refuse without troubling the provider.</returns>
public delegate ValueTask<bool> SignInPolicy(HttpContext http, CancellationToken ct);

/// <summary>
/// Resolves a verified external identity to whatever this application calls a user.
/// </summary>
/// <remarks>
/// The two payload parameters are named for what they are, and the naming is the guard. A bare
/// <c>(sub, email)</c> signature invites a find-or-create keyed on EMAIL, which lets anyone who
/// registers at the provider with a victim's address take over the local account. Provisioning keys
/// on the subject; the address is contact information and nothing else.
/// <para>
/// Optional. Without one the provider's subject is the session subject, which is the whole of a
/// product that keeps no local user table (R24).
/// </para>
/// </remarks>
/// <param name="http">The callback request, for anything the consumer needs from it.</param>
/// <param name="subject">The identity the provider verified. The only safe key.</param>
/// <param name="contact">The address the provider passed along. Proves nothing.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>The local user key, or null to refuse locally — in which case no session is minted.</returns>
public delegate ValueTask<string?> SignInResolver(
    HttpContext http,
    VerifiedSubject subject,
    UnverifiedContactInfo contact,
    CancellationToken ct);

/// <summary>The subject the provider verified. Stable across address changes; the key a local
/// account hangs on.</summary>
/// <param name="Value">The provider's <c>sub</c>.</param>
/// <remarks>
/// <b>A subject is only unique within one provider, and with a pairwise provider only within one
/// client.</b> A product configuring two schemes must store which scheme a subject came from
/// alongside it, or two providers' users collide on a shared key.
/// </remarks>
public readonly record struct VerifiedSubject(string Value);

/// <summary>Contact details the provider passed along. Not an identity, and never a lookup key.</summary>
/// <param name="Email">The address, when the provider sent one.</param>
public readonly record struct UnverifiedContactInfo(string? Email);

/// <summary>
/// What <c>AddSignIn</c> returns: the seams a consumer may take over.
/// </summary>
/// <remarks>
/// Every one of them is optional. A product that calls none gets in-memory stores, the provider's
/// own subject as the session subject, and no policy — which is a working sign-in for a
/// single-instance product and the shape the sample runs (R24).
/// </remarks>
public sealed class SignInBuilder
{
    private readonly IServiceCollection _services;

    internal SignInBuilder(IServiceCollection services, string scheme)
    {
        _services = services;
        Scheme = scheme;
    }

    /// <summary>The authentication scheme these registrations belong to.</summary>
    public string Scheme { get; }

    /// <summary>
    /// Registers the consumer's at-most-once storage for in-flight sign-ins, replacing the
    /// in-memory default.
    /// </summary>
    /// <typeparam name="TStore">The consumer's implementation.</typeparam>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Needed by anything running more than one process: the in-memory default cannot see a flow
    /// another instance started, so a callback landing on the wrong instance is refused.
    /// <para>
    /// The annotation is not decoration: DI constructs <typeparamref name="TStore"/> by picking a
    /// constructor at runtime, so the trimmer has to be told those constructors are reachable.
    /// Without it the type is trimmed to a shape DI cannot build and the failure lands at first
    /// request in a published binary, where nothing about the message says "trimming".
    /// </para>
    /// </remarks>
    public SignInBuilder WithFlowStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, ILoginFlowStore
    {
        _services.AddKeyedScoped<ILoginFlowStore, TStore>(Scheme);
        return this;
    }

    /// <summary>
    /// Registers the consumer's session storage, replacing the in-memory default.
    /// </summary>
    /// <typeparam name="TStore">The consumer's implementation.</typeparam>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Read <see cref="ISessionStore.ReplaceIfMatchAsync"/> before writing one: a read-then-write
    /// implementation passes every single-threaded test and destroys sessions under concurrency.
    /// <see cref="ISessionStore"/> is registered scoped, so an implementation may hold a database
    /// connection.
    /// </remarks>
    public SignInBuilder WithSessionStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, ISessionStore
    {
        _services.AddKeyedScoped<ISessionStore, TStore>(Scheme);
        return this;
    }

    /// <summary>
    /// Registers the callback that turns a verified provider subject into a local user key. A
    /// resolver returning null refuses the sign-in locally and mints no session.
    /// </summary>
    /// <param name="resolver">The consumer's resolver.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A resolver is already registered for this scheme.</exception>
    public SignInBuilder OnSignedIn(SignInResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        RefuseASecond(typeof(SignInResolver), nameof(OnSignedIn));
        _services.AddKeyedSingleton(Scheme, resolver);
        return this;
    }

    /// <summary>
    /// Registers the consumer's judgement on whether to begin a sign-in at all. Optional: without
    /// one, every attempt is begun.
    /// </summary>
    /// <param name="policy">The consumer's policy. See <see cref="SignInPolicy"/> for why it returns
    /// a boolean rather than a failure code.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A policy is already registered for this scheme.</exception>
    public SignInBuilder WithSignInPolicy(SignInPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        RefuseASecond(typeof(SignInPolicy), nameof(WithSignInPolicy));
        _services.AddKeyedSingleton(Scheme, policy);
        return this;
    }

    /// <summary>
    /// Refuses a SECOND registration rather than replacing the first.
    /// </summary>
    /// <remarks>
    /// <c>AddKeyedSingleton</c> appends and <c>GetKeyedService</c> returns the last registration, so
    /// without this a second call silently disables the first — a security control switched off by
    /// an ordinary merge, with nothing thrown at registration or at container build. A consumer
    /// wanting two rules composes them into one delegate, where the composition is visible.
    /// </remarks>
    private void RefuseASecond(Type serviceType, string method)
    {
        foreach (ServiceDescriptor descriptor in _services)
        {
            if (descriptor.ServiceType == serviceType
                && descriptor.IsKeyedService
                && string.Equals(descriptor.ServiceKey as string, Scheme, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"A {serviceType.Name} is already registered for the '{Scheme}' scheme. A second "
                    + $"{method} would silently replace the first; compose the two into one delegate "
                    + "instead.");
            }
        }
    }
}
