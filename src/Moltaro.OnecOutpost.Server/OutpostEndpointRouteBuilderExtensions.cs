using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Moltaro.OnecOutpost.Server.Endpoints;

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

        IServiceProvider services = endpoints.ServiceProvider;

        // Without AddOutpostServer there is no server and no handler. Resolving the server also refuses a container
        // without an ISignals, which the server needs.
        if (services.GetService<OutpostServer>() is null)
        {
            throw new InvalidOperationException(
                "MapOutpostAgents needs AddOutpostServer on the services of the application.");
        }

        // The library authenticates agents itself, and a poll is held for its hold, so neither the application's
        // authorization nor its request timeouts apply.
        RouteGroupBuilder group = endpoints.MapGroup(prefix);
        group.AllowAnonymous();
        group.DisableRequestTimeout();

        // Each handler takes the HttpContext alone, so that nothing binds the body before the checks of §9 of
        // docs/protocol.md.
        RequestDelegate pair = services.GetRequiredService<PairHandler>().HandleAsync;
        RequestDelegate poll = services.GetRequiredService<PollHandler>().HandleAsync;
        RequestDelegate outcome = services.GetRequiredService<OutcomeHandler>().HandleAsync;
        RequestDelegate unsupportedVersion = AgentAnswers.WriteUnsupportedVersionAsync;

        group.MapPost($"{OtherVersionConstraint.ServedVersion}/pair", pair);
        group.MapPost($"{OtherVersionConstraint.ServedVersion}/poll", poll);
        group.MapPost($"{OtherVersionConstraint.ServedVersion}/outcome", outcome);

        // Every other version gets the answer of §9 rather than a routing 404. The constraint never matches the served
        // version, so a path under it that names no request stays the application's.
        RoutePattern otherVersion = RoutePatternFactory.Parse(
            "{version}/{request}",
            defaults: null,
            parameterPolicies: new RouteValueDictionary { ["version"] = new OtherVersionConstraint() });

        group.Map(otherVersion, unsupportedVersion).WithMetadata(new HttpMethodMetadata([HttpMethods.Post]));

        return group;
    }
}
