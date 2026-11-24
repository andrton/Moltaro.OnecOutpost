using System.Text;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using Moltaro.OnecOutpost.TestSupport.FakeWindows;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;
using Moltaro.OnecOutpost.TestSupport.RunningAgent;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// An agent paired as the console pairs one, with its files in memory and fake COM workers. It connects directly to
// an HTTP server on loopback, or through a TestProxy that opens the TLS of every request.
internal sealed class LoopbackAgent
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private const string ProgramFolder = @"C:\Program Files\Moltaro Outpost";

    private LoopbackAgent(Guid id)
    {
        Id = id;
        Workers = new FakeWorkerLauncher(Data) { Runs = line => FakeRun.Gives(Echo(line.ParametersJson)) };
    }

    public Guid Id { get; }

    public MemoryDataFolder Data { get; } = new();

    public FakeAccessLists Access { get; } = new();

    public FakeProtection Protection { get; } = new();

    // The fake workers of every run: each command's result is its parameters, unless a test sets Runs.
    public FakeWorkerLauncher Workers { get; }

    // Pairs a new agent of the node with a token of the node's, as the console does.
    public static async Task<LoopbackAgent> PairAsync(
        LoopbackServer server,
        TestProxy proxy,
        CancellationToken cancellationToken)
    {
        OutpostPairingToken token = await server.Server.CreatePairingTokenAsync("Agent of the tests", cancellationToken);

        return await PairAsync(
            TestProxy.ServerAddress(server),
            token.Token,
            TestCalls.BaseKey,
            proxy.Uri,
            server.Clock,
            null,
            cancellationToken);
    }

    // A shell subprocess supplies the URL and token. Its first poll has a short hold so the accepted reply is an
    // observable event before the test starts a command; later polls take their timeout from the server's reply.
    public static Task<LoopbackAgent> PairAsync(Uri address, string token, string baseKey, CancellationToken cancellationToken)
    {
        return PairAsync(address, token, baseKey, null, TimeProvider.System, OutpostProtocol.MinPollTimeout, cancellationToken);
    }

    private static async Task<LoopbackAgent> PairAsync(
        Uri address,
        string token,
        string baseKey,
        Uri? proxy,
        TimeProvider clock,
        TimeSpan? initialPollTimeout,
        CancellationToken cancellationToken)
    {
        Assert.True(OutpostPairingTokenText.TryRead(token, out OutpostPairingTokenText? text));

        AgentPairingResult result;

        using (HttpMessageHandler handler = Handler(address, proxy))
        {
            result = await AgentPairing.PairAsync(
                address,
                text,
                PollRequests.AgentVersion,
                handler,
                RequestTimeout,
                clock,
                cancellationToken);
        }

        AgentPairingSettings settings = Assert.IsType<AgentPairingSettings>(result.Settings);
        if (initialPollTimeout is TimeSpan timeout)
        {
            settings = settings with { PollTimeout = timeout };
        }

        LoopbackAgent agent = new(settings.Agent);
        AgentSecrets secrets = new(
            Assert.IsType<byte[]>(result.PrivateKey),
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [(baseKey, OutpostModeEnum.Read)] = "read-pw",
                [(baseKey, OutpostModeEnum.Write)] = "write-pw",
            },
            new Dictionary<string, string>());

        agent.Data.Root.Seed("agent.json", AgentPairingFile.Write(settings));
        agent.Data.Root.Seed("secrets.dat", SecretsFile.Write(secrets, agent.Protection));
        agent.Data.Root.Seed("policy.json", Policy(baseKey, proxy));

        return agent;
    }

    // The handler of the program for the server and the proxy, which trusts the certificate of the test proxy.
    public static SocketsHttpHandler Handler(Uri server, Uri? proxy)
    {
        SocketsHttpHandler handler = ServerRoute.CreateHandler(server, proxy);
        handler.SslOptions.RemoteCertificateValidationCallback = TestProxy.ValidateCertificate;

        return handler;
    }

    // A run of the agent with its clock: the node's, or one of its own after the clock was set back.
    public HostRun Start(TimeProvider clock)
    {
        AgentParts parts = new(
            Data.Folders,
            Access,
            Protection,
            Workers,
            ProgramFolder,
            ProgramFolder + @"\OutpostExecutor.epf",
            Handler,
            clock,
            PollRequests.AgentVersion);

        return HostRun.Start(parts);
    }

    // A verified poll reply means its bases and lease have committed to the server's store.
    public Task WaitForConnectionAsync()
    {
        return Data.WaitForAsync(change => change.Line.StartsWith("append audit\\", StringComparison.Ordinal)
            && change.Bytes.AsSpan().IndexOf("\"entry\":\"connection\",\"state\":\"connected\""u8) >= 0);
    }

    // The recorded command inputs, without exposing the agent's worker line type to tests of the shell.
    public IReadOnlyList<(string Script, string ParametersJson, string? CallerKey)> Commands(OutpostModeEnum mode)
    {
        return [.. Workers.Processes.Where(process => process.Mode == mode)
            .SelectMany(process => process.Commands)
            .Select(command => (command.Script, command.ParametersJson, command.CallerKey))];
    }

    public void ReplyWith(byte[] outcome)
    {
        Workers.Runs = _ => FakeRun.Gives(outcome);
    }

    // The outcome of the executor for a script whose result is the parameters it was given.
    public static byte[] Echo(string parameters)
    {
        return Encoding.UTF8.GetBytes(
            $$"""{"kind":"succeeded","code":null,"result":{{parameters}},"errorText":null,"messages":[],"messagesTrimmed":false}""");
    }

    // A succeeded outcome whose result is a string of this many characters.
    public static byte[] Large(int length)
    {
        return Encoding.UTF8.GetBytes(
            $$"""{"kind":"succeeded","code":null,"result":"{{new string('x', length)}}","errorText":null,"messages":[],"messagesTrimmed":false}""");
    }

    private static string Policy(string baseKey, Uri? proxy)
    {
        string proxyMember = proxy is null ? "" : $"\"proxy\": \"{proxy}\",";

        return $$"""
            {
              {{proxyMember}}
              "bases": [
                {
                  "key": "{{baseKey}}",
                  "server": "srv-1c:1541",
                  "database": "accounting",
                  "read": { "account": "outpost-read" },
                  "write": { "account": "outpost-write" }
                }
              ]
            }
            """;
    }
}
