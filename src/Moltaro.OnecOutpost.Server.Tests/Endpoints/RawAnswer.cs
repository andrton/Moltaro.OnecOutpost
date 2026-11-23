namespace Moltaro.OnecOutpost.Server.Tests.Endpoints;

// What came back for a request of RawExchange: the status of the final answer, its Outpost-Signature when it has one,
// its body, and whether the body of the request was sent, which happens only when the server asked for it.
internal sealed record RawAnswer(int Status, string? Signature, byte[] Body, bool BodySent)
{
    // A status alone, without a body or an Outpost-Signature, as the brief of S4 defines a bare answer.
    public bool IsBare => Signature is null && Body.Length == 0;
}
