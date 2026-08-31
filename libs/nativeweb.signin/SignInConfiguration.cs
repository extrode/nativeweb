using System.Globalization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NativeWeb.SignIn;

/// <summary>
/// Fills <see cref="SignInOptions"/> from a configuration section (R17).
/// </summary>
/// <remarks>
/// <para>
/// Key by key, rather than <c>configuration.Bind(options)</c>. Bind walks the type with
/// reflection, which is an IL2026 in a library that has to publish NativeAOT clean; the
/// configuration binder's source generator fixes that in the app that calls it, not in a
/// library compiled before the app exists. Written out, the AOT story needs no caveats and
/// the set of keys a product may set is a list a reader can see.
/// </para>
/// <para>
/// An absent key leaves the option at its default, so a section naming only the four
/// endpoints and the client is a complete configuration. A present but unparseable value is
/// a <see cref="FormatException"/> at startup rather than a silent default: a
/// <c>SessionLifetime</c> of "fourteen days" that quietly means fourteen days is the kind of
/// mistake nobody finds until the sessions are wrong.
/// </para>
/// </remarks>
public static class SignInConfiguration
{
    /// <summary>Registers a sign-in provider under the default scheme, from configuration.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="section">The section holding the keys below.</param>
    /// <returns>The builder, for the optional seams.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
    public static SignInBuilder AddSignIn(this IServiceCollection services, IConfiguration section) =>
        services.AddSignIn(SessionAuthenticationHandler.SchemeName, section);

    /// <summary>Registers a sign-in provider under <paramref name="scheme"/>, from configuration.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="scheme">The authentication scheme name.</param>
    /// <param name="section">The section holding the keys below.</param>
    /// <returns>The builder, for the optional seams.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
    public static SignInBuilder AddSignIn(
        this IServiceCollection services,
        string scheme,
        IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);

        return services.AddSignIn(scheme, options => Fill(options, section));
    }

    /// <summary>Applies every key present in <paramref name="section"/> to <paramref name="options"/>.</summary>
    /// <param name="options">The options to fill.</param>
    /// <param name="section">The configuration section.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="FormatException">A key is present and cannot be read as its type.</exception>
    public static void Fill(SignInOptions options, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(section);

        options.AuthorizationEndpoint = Text(section, "AuthorizationEndpoint", options.AuthorizationEndpoint);
        options.TokenEndpoint = Text(section, "TokenEndpoint", options.TokenEndpoint);
        options.UserInfoEndpoint = Text(section, "UserInfoEndpoint", options.UserInfoEndpoint);
        options.IntrospectionEndpoint = Text(section, "IntrospectionEndpoint", options.IntrospectionEndpoint);

        options.ClientId = Text(section, "ClientId", options.ClientId);
        options.ClientSecret = Text(section, "ClientSecret", options.ClientSecret);
        options.RedirectUri = Text(section, "RedirectUri", options.RedirectUri);

        options.CookieName = Text(section, "CookieName", options.CookieName);
        options.LoginPath = Text(section, "LoginPath", options.LoginPath);
        options.CallbackPath = Text(section, "CallbackPath", options.CallbackPath);
        options.LogoutPath = Text(section, "LogoutPath", options.LogoutPath);
        options.SignedOutPath = Text(section, "SignedOutPath", options.SignedOutPath);
        options.DefaultReturnPath = Text(section, "DefaultReturnPath", options.DefaultReturnPath);

        options.ClientAuthentication = Mode(section, options.ClientAuthentication);
        options.RetainTokens = Flag(section, "RetainTokens", options.RetainTokens);
        options.AllowEphemeralStoreEncryptionKey =
            Flag(section, "AllowEphemeralStoreEncryptionKey", options.AllowEphemeralStoreEncryptionKey);

        options.FlowLifetime = Seconds(section, "FlowLifetimeSeconds", options.FlowLifetime);
        options.SessionLifetime = Seconds(section, "SessionLifetimeSeconds", options.SessionLifetime);
        options.RefreshSkew = Seconds(section, "RefreshSkewSeconds", options.RefreshSkew);
        options.IntrospectionCacheLifetime =
            Seconds(section, "IntrospectionCacheLifetimeSeconds", options.IntrospectionCacheLifetime);

        // Base64 rather than hex or a passphrase: it is what a secret manager hands back, and
        // the only encoding that survives an environment variable without a length surprise.
        if (section["StoreEncryptionKey"] is { Length: > 0 } key)
        {
            options.StoreEncryptionKey = Convert.FromBase64String(key);
        }

        // Replaced wholesale, not added to. A product asking for a narrower scope set than the
        // default cannot express that if the configured values are merged with it.
        IConfigurationSection scopes = section.GetSection("Scopes");

        if (scopes.GetChildren().Any())
        {
            options.Scopes.Clear();

            foreach (IConfigurationSection scope in scopes.GetChildren())
            {
                if (scope.Value is { Length: > 0 } value)
                {
                    options.Scopes.Add(value);
                }
            }
        }
    }

    // The section's own path when it has one, so a message names the key a product would
    // edit rather than the key this code reads. IConfiguration itself does not carry a path;
    // only IConfigurationSection does, and a root configuration passed straight in has none.
    private static string Where(IConfiguration section) =>
        section is IConfigurationSection { Path.Length: > 0 } named ? named.Path + ":" : "";

    private static string Text(IConfiguration section, string key, string fallback) =>
        section[key] is { Length: > 0 } value ? value : fallback;

    private static bool Flag(IConfiguration section, string key, bool fallback) =>
        section[key] is { Length: > 0 } value
            ? bool.TryParse(value, out bool parsed)
                ? parsed
                : throw new FormatException($"{Where(section)}{key} is '{value}', which is not true or false.")
            : fallback;

    private static TimeSpan Seconds(IConfiguration section, string key, TimeSpan fallback) =>
        section[key] is { Length: > 0 } value
            ? double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? TimeSpan.FromSeconds(parsed)
                : throw new FormatException($"{Where(section)}{key} is '{value}', which is not a number of seconds.")
            : fallback;

    private static ClientAuthenticationMode Mode(IConfiguration section, ClientAuthenticationMode fallback) =>
        section["ClientAuthentication"] is { Length: > 0 } value
            ? Enum.TryParse(value, ignoreCase: true, out ClientAuthenticationMode parsed)
                ? parsed
                : throw new FormatException(
                    $"{Where(section)}ClientAuthentication is '{value}'. It must be one of "
                        + string.Join(", ", Enum.GetNames<ClientAuthenticationMode>()) + ".")
            : fallback;
}
