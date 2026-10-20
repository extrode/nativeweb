namespace NativeWeb.Sample;

/// <summary>
/// The one address a contact submission is delivered to.
/// </summary>
/// <remarks>
/// A type rather than a raw string in the container, so it cannot be resolved by
/// accident by anything else that wants a string, and so the reason it exists travels
/// with it: the destination is configuration and never the address on the form. See
/// the registration in <c>Program</c>.
/// </remarks>
/// <param name="Address">The configured destination.</param>
internal sealed record ContactDestination(string Address);
