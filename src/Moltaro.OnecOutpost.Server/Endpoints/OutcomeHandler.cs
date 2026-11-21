using Microsoft.AspNetCore.Http;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server.Endpoints;

// The endpoint of `outcome` (§6.3 of docs/protocol.md). It makes the checks of §9 before the body, with the body limit
// of §10 that follows OutcomeSizeLimit, so that the web server's own default never refuses an outcome within it, and
// reads the body within that limit. What follows, the signature, the form and the confirmation, comes with the commands
// of S5: until then it throws NotSupportedException, which ASP.NET Core answers with a 500.
internal sealed class OutcomeHandler
{
    private readonly OutpostStore _store;

    // The validated copy that AddOutpostServer keeps; nothing changes it.
    private readonly OutpostServerOptions _options;

    public OutcomeHandler(OutpostStore store, OutpostServerOptions options)
    {
        _store = store;
        _options = options;
    }

    public async Task HandleAsync(HttpContext context)
    {
        int bodyLimit = _options.OutcomeSizeLimit + OutpostProtocol.OutcomeEnvelopeAllowance;

        if (!AgentRequests.CheckHeaders(context, out Guid agentId, out _)
            || !AgentRequests.CheckLength(context, bodyLimit))
        {
            return;
        }

        if (await AgentRequests.FindPairedAgentAsync(context, _store, agentId) is null)
        {
            return;
        }

        if (await AgentRequests.ReadBodyAsync(context) is null)
        {
            return;
        }

        throw new NotSupportedException();
    }
}
