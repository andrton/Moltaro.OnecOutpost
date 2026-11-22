using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Agent.Pairing;

// The form of agent.json: {"server":…,"agent":…,"serverKey":…,"pollTimeoutMs":…,"leaseGraceMs":…} in the forms of §2
// of docs/protocol.md: an absolute http or https URL that the transport's rule of §11 allows (ServerRoute.IsAllowed), an
// identity, the 91-byte key in base64url, and the two durations in the ranges of PollTimeout and LeaseGrace.
internal static class AgentPairingFile
{
    // Reads exactly these members, and throws FormatException, quoting nothing, for anything else.
    public static AgentPairingSettings Read(byte[] file)
    {
        try
        {
            using JsonDocument document = OutpostJsonReader.ParseMessage(file);
            JsonElement settings = OutpostJsonReader.Members(
                document.RootElement,
                "server",
                "agent",
                "serverKey",
                "pollTimeoutMs",
                "leaseGraceMs");

            Uri server = ReadServer(settings);
            Guid agent = OutpostJsonReader.ReadId(settings, "agent");
            byte[] serverKey = OutpostJsonReader.ReadBytes(settings, "serverKey");

            // The import refuses every key but the 91-byte form of §2 on P-256. The key is imported again where it
            // verifies, so this one is only a check.
            OutpostKeys.ImportPublicKey(serverKey).Dispose();

            TimeSpan pollTimeout = OutpostJsonReader.ReadDuration(
                settings,
                "pollTimeoutMs",
                OutpostProtocol.MinPollTimeout,
                OutpostProtocol.MaxPollTimeout);
            TimeSpan leaseGrace = OutpostJsonReader.ReadDuration(
                settings,
                "leaseGraceMs",
                OutpostProtocol.MinLeaseGrace,
                OutpostProtocol.MaxLeaseGrace);

            return new AgentPairingSettings(server, agent, serverKey, pollTimeout, leaseGrace);
        }
        catch (OutpostProtocolException exception)
        {
            // The reasons of the reader name members, never their values.
            throw new FormatException($"The content is not in the form of agent.json: {exception.Message}");
        }
    }

    // Writes the settings with the writer of §2.
    public static byte[] Write(AgentPairingSettings settings)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("server");
        writer.String(settings.Server.AbsoluteUri);
        writer.Name("agent");
        writer.Id(settings.Agent);
        writer.Name("serverKey");
        writer.Bytes(settings.ServerKey);
        writer.Name("pollTimeoutMs");
        writer.Duration(settings.PollTimeout);
        writer.Name("leaseGraceMs");
        writer.Duration(settings.LeaseGrace);
        writer.EndObject();

        return writer.ToArray();
    }

    // An absolute http or https URL that §11 allows, so that a start records an address the agent never reaches. The
    // string is named by the rule it breaks, never repeated.
    private static Uri ReadServer(JsonElement settings)
    {
        string text = OutpostJsonReader.ReadString(settings, "server");

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? server)
            || (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps))
        {
            throw OutpostJsonReader.Form("The member server is not an absolute http or https URL.");
        }

        if (!ServerRoute.IsAllowed(server))
        {
            throw OutpostJsonReader.Form("The member server is an http URL whose host is not a loopback address (§11 of the protocol).");
        }

        return server;
    }
}
