using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Moltaro.OnecOutpost.Server.Endpoints;

// The route constraint of the endpoint that answers a version the server does not speak (§9 of docs/protocol.md): it
// matches every version but the one served, compared without regard to case, as routing compares the literal segment
// of the served paths. A path under the served version that names no request of the protocol is never matched, so it
// stays the application's.
internal sealed class OtherVersionConstraint : IRouteConstraint
{
    // The version of the paths that this build serves (§1).
    public const string ServedVersion = "v1";

    public bool Match(
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection)
    {
        return values.TryGetValue(routeKey, out object? value)
            && value is string version
            && !string.Equals(version, ServedVersion, StringComparison.OrdinalIgnoreCase);
    }
}
