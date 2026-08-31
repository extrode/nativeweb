namespace NativeWeb.SignIn;

/// <summary>
/// The storage seam for a signed-in session. The library defines no schema and issues no query;
/// the consumer owns the table behind this interface.
/// </summary>
/// <remarks>
/// Every row is addressed by <c>keyHash</c> — <c>SHA-256</c> of the opaque cookie value, never the
/// value itself. That is the whole of R8: a dump of this store, its backups or its replication
/// stream yields nothing that can be presented as a cookie.
/// </remarks>
public interface ISessionStore
{
    /// <summary>Records a newly minted session.</summary>
    /// <param name="keyHash">The hash of the cookie value. The row's key.</param>
    /// <param name="record">What the session holds.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateAsync(string keyHash, SessionRecord record, CancellationToken ct);

    /// <summary>Reads a session, or <see langword="null"/> when there is none.</summary>
    /// <param name="keyHash">The hash of the cookie value presented by the browser.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored session, expired or not. Expiry is the caller's to judge.</returns>
    Task<SessionRecord?> GetAsync(string keyHash, CancellationToken ct);

    /// <summary>
    /// Replaces a session's stored tokens, but only if its refresh token is still the one the
    /// caller read. An ATOMIC compare-and-swap: one statement that both compares and writes.
    /// </summary>
    /// <remarks>
    /// This is the single most dangerous method in the library to implement casually. A
    /// read-then-write implementation compiles, passes every single-threaded test, and destroys
    /// sessions in production.
    /// <para>
    /// The failure: two requests on one session both find the access token expired and both
    /// present the SAME refresh token to a provider that rotates. The provider consumes the first
    /// and reads the second as a replayed token — which is the signature of a stolen one — so it
    /// revokes the entire token family, and a healthy session is dead. Nothing in the logs says
    /// why; the customer is signed out mid-task and signing in again works.
    /// </para>
    /// <para>
    /// Returning <see cref="SessionReplace.Conflict"/> is how the loser of that race is told to
    /// re-read the row and use the winner's fresh token instead of calling the provider. A store
    /// that reads, compares in application code, and then writes has a window between the compare
    /// and the write in which both callers see a match and both proceed. Use
    /// <c>UPDATE … WHERE refresh_token_hash = @expected</c> and report on the row count, or the
    /// equivalent single atomic operation for whatever holds the row.
    /// </para>
    /// </remarks>
    /// <param name="keyHash">The hash of the cookie value.</param>
    /// <param name="expectedRefreshTokenHash">
    /// <see cref="SessionRecord.RefreshTokenHash"/> as it was when the caller read the row.
    /// </param>
    /// <param name="replacement">The session as it should be after a successful refresh.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="SessionReplace.Replaced"/> for the one caller that won,
    /// <see cref="SessionReplace.Conflict"/> for every caller whose expected hash no longer
    /// matches, and <see cref="SessionReplace.Gone"/> when the row has been removed.
    /// </returns>
    Task<SessionReplace> ReplaceIfMatchAsync(
        string keyHash,
        string expectedRefreshTokenHash,
        SessionRecord replacement,
        CancellationToken ct);

    /// <summary>Ends a session. Removing a row that is not there is not an error.</summary>
    /// <param name="keyHash">The hash of the cookie value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveAsync(string keyHash, CancellationToken ct);

    /// <summary>
    /// Deletes sessions past their expiry. Garbage collection only — no security property depends
    /// on this having run, and none may be moved here. Expiry is enforced when a session is read.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task SweepAsync(CancellationToken ct);
}

/// <summary>What a <see cref="ISessionStore.ReplaceIfMatchAsync"/> did.</summary>
public enum SessionReplace
{
    /// <summary>This caller's expected hash matched and the row now holds the replacement.</summary>
    Replaced,

    /// <summary>
    /// The row exists but holds a different refresh token: somebody else refreshed first. Re-read
    /// and continue on their tokens. Do NOT call the provider again.
    /// </summary>
    Conflict,

    /// <summary>There is no such row. The session ended while this request was in flight.</summary>
    Gone,
}

/// <summary>
/// What a session holds. Every token here is already encrypted by the caller; a store neither
/// encrypts nor decrypts, and must not log any of these fields.
/// </summary>
/// <param name="Subject">The provider's subject identifier for the signed-in user.</param>
/// <param name="IssuedAt">When the session was minted.</param>
/// <param name="ExpiresAt">When the session stops being accepted, whatever the provider says.</param>
/// <param name="ProtectedAccessToken">
/// The provider's access token, encrypted. Empty when the product opted out of retaining tokens.
/// </param>
/// <param name="ProtectedRefreshToken">
/// The provider's refresh token, encrypted. Empty when the provider issued none, or when the
/// product opted out of retaining tokens.
/// </param>
/// <param name="AccessTokenExpiresAt">When the access token stops working, as the provider reported it.</param>
/// <param name="RefreshTokenHash">
/// The hash of the plaintext refresh token, which is what
/// <see cref="ISessionStore.ReplaceIfMatchAsync"/> compares. A hash rather than the ciphertext
/// because encryption is randomised: two encryptions of one token differ, so comparing ciphertext
/// would never match and every refresh would read as a conflict.
/// </param>
public sealed record SessionRecord(
    string Subject,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string ProtectedAccessToken,
    string ProtectedRefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshTokenHash)
{
    /// <summary>
    /// Redacts both ciphertexts and the refresh-token hash. None is usable on its own, but all
    /// three are session material, and a record whose <c>ToString</c> prints them writes them into
    /// every log line and exception message that mentions the session.
    /// </summary>
    public override string ToString() =>
        $"SessionRecord {{ Subject = {Subject}, IssuedAt = {IssuedAt:O}, ExpiresAt = {ExpiresAt:O}, "
        + "ProtectedAccessToken = (redacted), ProtectedRefreshToken = (redacted), "
        + $"AccessTokenExpiresAt = {AccessTokenExpiresAt:O}, RefreshTokenHash = (redacted) }}";
}
