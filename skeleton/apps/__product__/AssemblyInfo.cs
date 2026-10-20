using System.Runtime.CompilerServices;

// The test projects exercise internal middleware types directly (e.g.
// ErrorPageMiddleware.Describe, SecurityHeadersMiddleware.StaticPageCsp)
// rather than through reflection; same grant the NativeWeb repo's apps make.
[assembly: InternalsVisibleTo("__product__.tests.unit")]
[assembly: InternalsVisibleTo("__product__.tests.integration")]
