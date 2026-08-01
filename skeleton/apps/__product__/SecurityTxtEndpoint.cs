namespace __PRODUCT__;

// RFC 9116 security.txt: where a researcher who found a vulnerability should
// report it. Expires is mandatory (the RFC recommends under a year); a stale
// date is this file's way of saying "no longer maintained", so the constant
// below needs a yearly bump -- deliberately a visible literal rather than
// computed from the clock, which would defeat the staleness signal.
// Skeleton copy -- the product owns this file after stamping.
internal static class SecurityTxtEndpoint
{
    // Placeholder: replace the contact address with this product's real
    // reporting mailbox (and the domain with its real one) before shipping.
    private const string Body =
        "Contact: mailto:security@__product__.example\n"
        + "Expires: 2027-08-01T00:00:00Z\n"
        + "Preferred-Languages: en\n";

    public static void MapSecurityTxtEndpoint(this WebApplication app)
    {
        // The canonical location is /.well-known/security.txt; the RFC also
        // permits a top-level /security.txt for legacy scanners.
        app.MapGet("/.well-known/security.txt", () => Results.Text(Body, "text/plain"));
        app.MapGet("/security.txt", () => Results.Text(Body, "text/plain"));
    }
}
