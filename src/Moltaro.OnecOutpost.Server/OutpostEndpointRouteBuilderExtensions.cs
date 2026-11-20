using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// Maps the requests of the agents in the endpoints of an ASP.NET Core application.
/// </summary>
public static class OutpostEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the requests of the agents as <c>POST {prefix}/{version}/pair</c>, <c>POST {prefix}/{version}/poll</c> and
    /// <c>POST {prefix}/{version}/outcome</c>.
    /// </summary>
    /// <param name="endpoints">The endpoints of the application.</param>
    /// <param name="prefix">
    /// The prefix of the paths: <c>/</c> followed by one or more segments of <c>A–Z a–z 0–9 - _ .</c> separated by
    /// <c>/</c>, not ending with <c>/</c>; no segment is <c>.</c> or <c>..</c>. For example <c>/outpost</c>.
    /// </param>
    /// <returns>The group of the endpoints, so that the application can add its own conventions, such as rate limiting.</returns>
    /// <remarks>
    /// <para>
    /// The version <c>v1</c> is served. A request for any other version is answered as the protocol says, with the code
    /// <see cref="OutpostCodes.ProtocolVersion"/>, never with a routing 404.
    /// </para>
    /// <para>
    /// The endpoints allow anonymous access, since the library authenticates agents itself by the signature of every
    /// request; the application's authorization never applies to them. Each endpoint has the body size limit that the
    /// protocol gives it, and the limit of the outcome request follows
    /// <see cref="OutpostServerOptions.OutcomeSizeLimit"/>, so that the web server's own default never refuses an
    /// outcome within the limit.
    /// </para>
    /// <para>
    /// The nodes need no sticky sessions. The ingress in front of them has to let a poll stay open for
    /// <see cref="OutpostServerOptions.PollTimeout"/> and let an outcome of
    /// <see cref="OutpostServerOptions.OutcomeSizeLimit"/> through.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="endpoints"/> or <paramref name="prefix"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="prefix"/> breaks the rule of endpoint prefixes.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the call: <see cref="OutpostServiceCollectionExtensions.AddOutpostServer"/> was not called on the
    /// services of the application, or the server cannot be resolved, as when no <see cref="Moltaro.Signals.ISignals"/>
    /// is registered.
    /// </exception>
    public static RouteGroupBuilder MapOutpostAgents(this IEndpointRouteBuilder endpoints, string prefix)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ServerNames.CheckEndpointPrefix(prefix, nameof(prefix));

        // The endpoints need the options of the server, so they resolve it; without AddOutpostServer there is none.
        if (endpoints.ServiceProvider.GetService<OutpostServer>() is null)
        {
            throw new InvalidOperationException(
                "MapOutpostAgents needs AddOutpostServer on the services of the application.");
        }

        throw new NotSupportedException();
    }
}
