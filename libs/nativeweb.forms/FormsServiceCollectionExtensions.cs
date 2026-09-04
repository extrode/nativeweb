using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NativeWeb.Forms;

/// <summary>
/// Registers what a form needs beyond its generated binder.
/// </summary>
/// <remarks>
/// <para>
/// The binder needs nothing registered: it is emitted onto the model and reads the
/// request. <see cref="CarriedAnswers"/> is the exception, because it holds a
/// protector derived from the host's key ring, so it has to come from the container.
/// </para>
/// <para>
/// Not called by <c>AddNativeWebWeb</c>. A host that uses no multi-step form should
/// not carry the type, and a host that does says so in one line.
/// </para>
/// </remarks>
public static class FormsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CarriedAnswers"/> over the host's data protection.
    /// </summary>
    /// <remarks>
    /// Singleton, because the protector it holds is one: deriving a fresh protector
    /// per request would be work for no property, since the key ring behind it is
    /// shared either way.
    /// </remarks>
    /// <param name="services">The container being configured.</param>
    /// <returns>The same container.</returns>
    /// <exception cref="InvalidOperationException">
    /// Data protection has not been added. Thrown at registration, not at the first
    /// form post: a host that has this wrong should not start.
    /// </exception>
    public static IServiceCollection AddNativeWebForms(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The check is on the descriptor rather than on a built provider, because
        // building one here would instantiate every singleton the host has registered
        // so far, in the middle of registration.
        bool protection = services.Any(d => d.ServiceType == typeof(IDataProtectionProvider));

        if (!protection)
        {
            throw new InvalidOperationException(
                "AddNativeWebForms() needs data protection, which carries the key that makes a "
                + "multi-step form's hidden field unreadable and unalterable. Call "
                + "AddNativeWebDataProtection(options) -- AddNativeWebWeb does -- before this.");
        }

        services.TryAddSingleton<CarriedAnswers>();
        return services;
    }
}
