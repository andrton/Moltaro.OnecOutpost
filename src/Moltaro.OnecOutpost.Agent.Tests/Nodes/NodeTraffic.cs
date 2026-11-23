using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Tests.EndToEnd;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Agent.Tests.Nodes;

// What the tests of several nodes read of the requests that the proxy saw and of the answers that a node gave: the kind
// of a request, the number and the received commands of a poll from its body (§6.2 of docs/protocol.md), by which a
// test chooses the request that the proxy drops or holds; and a poll answer verified with the server key that the
// agent pinned (§9), with its commands, each verified as §7 says.
internal static class NodeTraffic
{
    public static bool IsPoll(ProxiedRequest request)
    {
        return request.Kind == OutpostRequestKindEnum.Poll;
    }

    public static bool IsOutcome(ProxiedRequest request)
    {
        return request.Kind == OutpostRequestKindEnum.Outcome;
    }

    // A match of a poll that lists the command among its received commands.
    public static Func<ProxiedRequest, bool> PollListing(Guid command)
    {
        return request => IsPoll(request) && Received(request).Contains(command);
    }

    // The polls that the proxy read, in their order.
    public static List<ProxiedRequest> ProxiedPolls(TestProxy proxy)
    {
        return [.. proxy.Requests.Where(IsPoll)];
    }

    // The outcome requests that the proxy read, in their order.
    public static List<ProxiedRequest> ProxiedOutcomes(TestProxy proxy)
    {
        return [.. proxy.Requests.Where(IsOutcome)];
    }

    public static long Number(ProxiedRequest poll)
    {
        using JsonDocument body = JsonDocument.Parse(poll.Body);

        return body.RootElement.GetProperty("number").GetInt64();
    }

    public static List<Guid> Received(ProxiedRequest poll)
    {
        using JsonDocument body = JsonDocument.Parse(poll.Body);

        return [.. body.RootElement.GetProperty("received").EnumerateArray().Select(entry => entry.GetGuid())];
    }

    // The answer, a verified 200 to its poll, read with the server key that agent.json holds.
    public static OutpostPollAnswer ReadPollAnswer(LoopbackAgent agent, AgentAnswer answer)
    {
        Assert.Equal(200, answer.Status);

        using ECDsa serverKey = PinnedKey(agent);

        return OutpostCodec.ReadPollAnswer(
            serverKey,
            agent.Id,
            Free(answer.RequestBody),
            answer.RequestBody,
            answer.Body,
            answer.SignatureBytes);
    }

    // The commands that a verified poll answer hands out, in its order, each verified and read as §7 says.
    public static List<OutpostCommandMessage> CommandsOf(LoopbackAgent agent, AgentAnswer answer)
    {
        OutpostPollAnswer read = ReadPollAnswer(agent, answer);

        using ECDsa serverKey = PinnedKey(agent);

        return [.. read.Commands.Select(command => OutpostCodec.ReadCommand(serverKey, command))];
    }

    private static ECDsa PinnedKey(LoopbackAgent agent)
    {
        AgentPairingSettings settings = AgentPairingFile.Read(agent.Data.Root.Bytes("agent.json"));

        return OutpostKeys.ImportPublicKey(settings.ServerKey);
    }

    // The sum of free over the modes of a poll's bases, which bounds the commands of its answer.
    private static int Free(byte[] pollBody)
    {
        using JsonDocument body = JsonDocument.Parse(pollBody);
        int free = 0;

        foreach (JsonElement declared in body.RootElement.GetProperty("bases").EnumerateArray())
        {
            free += declared.GetProperty("read").GetProperty("free").GetInt32();

            JsonElement write = declared.GetProperty("write");

            if (write.ValueKind == JsonValueKind.Object)
            {
                free += write.GetProperty("free").GetInt32();
            }
        }

        return free;
    }
}
