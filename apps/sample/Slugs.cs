using System.Globalization;
using System.Text;

namespace NativeWeb.Sample;

/// <summary>
/// The one definition of what an article's public address may be made of, and how
/// one is derived from a title when the admin leaves it blank.
/// </summary>
/// <remarks>
/// <para>
/// The alphabet is lowercase ASCII letters, digits and single hyphens between
/// them: <c>^[a-z0-9]+(-[a-z0-9]+)*$</c>. That is what every address bar, feed
/// reader and crawler treats as plain, and it is the CHECK constraint
/// <c>db/sample/migrations/0003_slugs_and_tags.sql</c> puts on the column, so the
/// database refuses what this class would.
/// </para>
/// <para>
/// Derivation lowercases, keeps ASCII letters and digits, collapses every other
/// run into one hyphen, trims the ends and caps the length. A title of nothing but
/// punctuation, or one in a script with no ASCII letters, derives to
/// <see cref="Fallback"/>; the store then makes it unique the same way it does any
/// collision. Transliteration is not attempted: an admin who wants a readable
/// address for such a title types one.
/// </para>
/// </remarks>
internal static class Slugs
{
    /// <summary>The pattern a valid slug matches, as the migration's CHECK states it.</summary>
    public const string Pattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    /// <summary>What a title with nothing usable in it derives to.</summary>
    public const string Fallback = "article";

    /// <summary>
    /// The longest slug <see cref="Derive"/> produces. A typed slug may be longer,
    /// and so may a derived one once <see cref="Unique"/> adds its suffix.
    /// </summary>
    public const int MaxDerivedLength = 80;

    public const string InvalidMessage = "Slug may contain only lowercase letters, digits and single hyphens.";

    public static string TakenMessage(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"Slug '{slug}' is already used.");

    public static bool IsValid(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        bool previousWasHyphen = true;
        foreach (char c in candidate)
        {
            if (c == '-')
            {
                if (previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
                continue;
            }

            if (!IsPlain(c))
            {
                return false;
            }

            previousWasHyphen = false;
        }

        return !previousWasHyphen;
    }

    public static string Derive(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return Fallback;
        }

        var sb = new StringBuilder(title.Length);
        bool pendingHyphen = false;
        foreach (char raw in title.ToLowerInvariant())
        {
            if (IsPlain(raw))
            {
                if (pendingHyphen && sb.Length > 0)
                {
                    sb.Append('-');
                }

                sb.Append(raw);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }

            if (sb.Length >= MaxDerivedLength)
            {
                break;
            }
        }

        string derived = sb.ToString().TrimEnd('-');
        return derived.Length == 0 ? Fallback : derived;
    }

    /// <summary>
    /// <paramref name="wanted"/> if <paramref name="taken"/> says it is free, else the
    /// first of <c>wanted-2</c>, <c>wanted-3</c>, ... that is.
    /// </summary>
    public static string Unique(string wanted, Func<string, bool> taken)
    {
        if (!taken(wanted))
        {
            return wanted;
        }

        for (int n = 2; ; n++)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"{wanted}-{n}");
            if (!taken(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsPlain(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9');
}
