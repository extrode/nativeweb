using System;
using System.Globalization;
using System.Text;

namespace NativeWeb.Permissions.Generator;

/// <summary>
/// The one rule that turns an assembly scope, a model type name and an action into
/// a permission name and into the identifier of the constant that holds it.
/// </summary>
/// <remarks>
/// <para><b>Compiled into two generators.</b> The permissions generator emits the
/// constants; the scaffold generator has to name the very same constant when it
/// gates a route, and cannot read the other generator's output because a generator
/// never sees another generator's sources in the same compilation. So both derive
/// the name, and this file is linked into the scaffold generator by
/// <c>&lt;Compile Include&gt;</c> so that there is one rule rather than two that
/// agree today.</para>
/// <para>What the linking buys is a failure mode. The scaffold generator emits a
/// reference to the constant, never the permission string, so a divergence between
/// the two is CS0117 at build time instead of a route silently gated on a name
/// nobody can be granted.</para>
/// </remarks>
public static class PermissionNaming
{
    /// <summary>Separates the assembly scope from the rest of the name.</summary>
    /// <remarks>
    /// A colon, not a dot, so that a dot inside an assembly name is unambiguous:
    /// <c>my.app:article.view</c> splits at the first colon and nowhere else.
    /// </remarks>
    public const char ScopeSeparator = ':';

    /// <summary>The four actions every marked model gets without being asked.</summary>
    public static readonly string[] CoreActions = { "view", "add", "change", "delete" };

    /// <summary>Suffix on the generated per-model constants class.</summary>
    public const string ConstantsClassSuffix = "Permissions";

    /// <summary>
    /// True when <paramref name="scope"/> is usable as an assembly scope:
    /// <c>[a-z][a-z0-9_.]*</c>.
    /// </summary>
    public static bool IsValidScope(string? scope)
    {
        if (string.IsNullOrEmpty(scope) || !IsLower(scope![0]))
        {
            return false;
        }

        for (int index = 1; index < scope.Length; index++)
        {
            char character = scope[index];

            if (!IsLower(character) && !IsDigit(character) && character != '_' && character != '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="action"/> is usable as a permission action:
    /// <c>[a-z][a-z0-9_]*</c>. No dot, because a dot separates the model from the
    /// action, and no colon, because a colon separates the scope.
    /// </summary>
    public static bool IsValidAction(string? action)
    {
        if (string.IsNullOrEmpty(action) || !IsLower(action![0]))
        {
            return false;
        }

        for (int index = 1; index < action.Length; index++)
        {
            char character = action[index];

            if (!IsLower(character) && !IsDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when <paramref name="action"/> is one of the four core actions.</summary>
    public static bool IsCoreAction(string action) =>
        Array.IndexOf(CoreActions, action) >= 0;

    /// <summary>
    /// The scope an assembly with no explicit <c>[PermissionScope]</c> falls back
    /// to: its own name, lower-cased. Null when the assembly name cannot form a
    /// valid scope, which is the case the generator reports a diagnostic for rather
    /// than guessing.
    /// </summary>
    public static string? DefaultScope(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return null;
        }

        string lowered = assemblyName!.ToLowerInvariant();
        return IsValidScope(lowered) ? lowered : null;
    }

    /// <summary>The model segment of a permission name: the simple type name, lower-cased.</summary>
    /// <remarks>
    /// Not pluralised. <c>Article</c> is <c>article</c>, which reads the same way
    /// Django's codenames do, and pluralisation is a guess that English loses.
    /// </remarks>
    public static string ModelSegment(string typeName) => typeName.ToLowerInvariant();

    /// <summary>
    /// The full permission name, <c>{scope}:{model}.{action}</c>.
    /// </summary>
    public static string Compose(string scope, string typeName, string action) =>
        scope + ScopeSeparator + ModelSegment(typeName) + "." + action;

    /// <summary>The name of the generated constants class for a model: <c>ArticlePermissions</c>.</summary>
    public static string ConstantsClassName(string typeName) => typeName + ConstantsClassSuffix;

    /// <summary>
    /// The identifier of the constant holding one action: <c>Change</c> for
    /// <c>change</c>, <c>PublishDraft</c> for <c>publish_draft</c>.
    /// </summary>
    public static string ConstantName(string action)
    {
        var built = new StringBuilder(action.Length);
        bool startOfWord = true;

        foreach (char character in action)
        {
            if (character == '_')
            {
                startOfWord = true;
                continue;
            }

            built.Append(startOfWord ? char.ToUpper(character, CultureInfo.InvariantCulture) : character);
            startOfWord = false;
        }

        return built.ToString();
    }

    private static bool IsLower(char character) => character is >= 'a' and <= 'z';

    private static bool IsDigit(char character) => character is >= '0' and <= '9';
}
