using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server.Endpoints;

// The checks of §9 of docs/protocol.md that the endpoints make before they read the body of a request, and the reading
// of the body. The version of the path, the first check, is the routing's. A check that fails answers with its bare
// status and gives false or null, and the handler then returns: the first check that fails decides the answer, and no
// body of a poll or an outcome is read before its agent is known to have a key.
internal static class AgentRequests
{
    // Check 2, the headers of §4: Content-Type, Outpost-Agent and Outpost-Signature, each once and in its form, and no
    // Content-Encoding at all (§2). A bare 400 otherwise. No other header is required or checked.
    public static bool CheckHeaders(HttpContext context, out Guid agent, [NotNullWhen(true)] out byte[]? signature)
    {
        IHeaderDictionary headers = context.Request.Headers;

        agent = Guid.Empty;
        signature = null;

        bool valid = TryGetOne(headers.ContentType, out string? contentType)
            && contentType == MediaTypeNames.Application.Json
            && TryGetOne(headers[OutpostProtocol.AgentHeader], out string? agentHeader)
            && OutpostHeaders.TryParseAgent(agentHeader, out agent)
            && TryGetOne(headers[OutpostProtocol.SignatureHeader], out string? signatureHeader)
            && OutpostHeaders.TryParseSignature(signatureHeader, out signature)
            && headers.ContentEncoding.Count == 0;

        if (!valid)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status400BadRequest);
        }

        return valid;
    }

    // Check 3, the length against its limit of §10. The limit is set on the request, so that Kestrel applies it while
    // the body is read, also to a body that declares no length; a declared length beyond it gets a bare 413 at once,
    // before the body is read, and so before Kestrel asks a client that sent Expect: 100-continue for it.
    //
    // The feature is missing or read-only when something before the endpoint, such as a middleware that buffers the
    // body, has read it already. The request then fails loudly instead of being read against another limit.
    public static bool CheckLength(HttpContext context, long limit)
    {
        IHttpMaxRequestBodySizeFeature? bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (bodySize is null || bodySize.IsReadOnly)
        {
            throw new InvalidOperationException(
                "The endpoints of the Outpost agents need to set the body size limit of their requests, and the server or a middleware before them does not let them.");
        }

        bodySize.MaxRequestBodySize = limit;

        if (context.Request.ContentLength > limit)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status413PayloadTooLarge);

            return false;
        }

        return true;
    }

    // Checks 4.1 and 4.2, for poll and outcome: the agent is known and has a key, or a bare 401, and is not revoked, or a
    // bare 403. The row of the paired agent otherwise, with its key.
    public static async Task<AgentRow?> FindPairedAgentAsync(HttpContext context, OutpostStore store, Guid agentId)
    {
        AgentRow? agent = await store.FindAgentAsync(agentId, context.RequestAborted);

        if (agent is null || agent.State == OutpostAgentStateEnum.Pending)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status401Unauthorized);

            return null;
        }

        if (agent.State == OutpostAgentStateEnum.Revoked)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status403Forbidden);

            return null;
        }

        return agent;
    }

    // Reads the body whole, into one array, within the limit that CheckLength set. A body that declares no length and
    // grows beyond the limit while it is read makes Kestrel throw its 413, which becomes the bare 413 here, so that the
    // exception handler of the application never decides the answer; null then.
    //
    // The buffer grows with the bytes that came, never with the length a request declares, so that a request that
    // declares the limit and sends nothing holds no memory for it.
    public static async Task<byte[]?> ReadBodyAsync(HttpContext context)
    {
        using MemoryStream body = new();

        try
        {
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status413PayloadTooLarge);

            return null;
        }

        return body.ToArray();
    }

    // The value of a header that came exactly once, on one line.
    private static bool TryGetOne(StringValues values, [NotNullWhen(true)] out string? value)
    {
        value = values.Count == 1 ? values[0] : null;

        return value is not null;
    }
}
