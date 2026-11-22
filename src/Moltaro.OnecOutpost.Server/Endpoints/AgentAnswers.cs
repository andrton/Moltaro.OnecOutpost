using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Signing;

namespace Moltaro.OnecOutpost.Server.Endpoints;

// The answers of the endpoints to the agents (§9 of docs/protocol.md): bare, a status alone without a body or an
// Outpost-Signature; the one unsigned body, for a version the server does not speak; and signed, a body with the
// server's signature over the request it answers (§3). Every body is application/json, written as the exact bytes
// that were signed, without a content coding (§2).
internal static class AgentAnswers
{
    public static void WriteBare(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
    }

    // The 400 for a path that names a version other than v1, with the body that every later version keeps. It reads
    // nothing of the request, so its body is never sent when the agent waits for 100 Continue.
    public static async Task WriteUnsupportedVersionAsync(HttpContext context)
    {
        await WriteBodyAsync(context, StatusCodes.Status400BadRequest, OutpostCodec.WriteUnsupportedVersion());
    }

    // An answer signed over the kind and the agent of the request, the status, and the exact bytes of the request body
    // and of the answer body (§3).
    public static async Task WriteSignedAsync(
        HttpContext context,
        ServerSigningKey signingKey,
        OutpostRequestKindEnum kind,
        Guid agent,
        int status,
        byte[] requestBody,
        byte[] answerBody)
    {
        byte[] signature = signingKey.SignAnswer(kind, agent, status, requestBody, answerBody);

        context.Response.Headers[OutpostProtocol.SignatureHeader] = OutpostHeaders.FormatSignature(signature);

        await WriteBodyAsync(context, status, answerBody);
    }

    // A signed refusal of §9 with its code, under the status that the table of §9 gives it.
    public static async Task WriteRefusalAsync(
        HttpContext context,
        ServerSigningKey signingKey,
        OutpostRequestKindEnum kind,
        Guid agent,
        byte[] requestBody,
        string code,
        DateTimeOffset time)
    {
        int status = code switch
        {
            OutpostProtocolCodes.Form => StatusCodes.Status400BadRequest,
            OutpostProtocolCodes.Signature => StatusCodes.Status401Unauthorized,
            OutpostProtocolCodes.ResultTooLarge => StatusCodes.Status413PayloadTooLarge,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The code is not one of a signed refusal."),
        };

        byte[] answerBody = OutpostCodec.Write(new OutpostRefusal(time, code));

        await WriteSignedAsync(context, signingKey, kind, agent, status, requestBody, answerBody);
    }

    private static async Task WriteBodyAsync(HttpContext context, int status, byte[] body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        context.Response.ContentLength = body.Length;

        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }
}
