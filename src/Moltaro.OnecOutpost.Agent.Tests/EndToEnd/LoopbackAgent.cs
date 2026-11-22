using System.Text;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Agent.Tests.FakeWindows;
using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Tests.Host;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Agent.Tests.EndToEnd;

// An agent paired with a LoopbackServer through a TestProxy, as the console pairs one: AgentPairing over the handler of
// the program with the proxy of the policy, then agent.json, secrets.dat and policy.json in a data folder in memory.
// agent.json names the node as https://outpost.test:{port}/outpost/ and policy.json the test proxy, so that every
// request goes by CONNECT through the proxy, which opens TLS as a hop does (the S6 brief, "S6c's tests"). Its runs are
// AgentHost.RunAsync with the fake access lists and protection, the fake workers in place of the COM workers, and the
// handler of the program, which accepts the proxy's certificate in place of the system's validation. The policy serves
// the base of TestCalls for Read and Write, one worker each.
internal sealed class LoopbackAgent
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

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
        Assert.True(OutpostPairingTokenText.TryRead(token.Token, out OutpostPairingTokenText? text));
        Uri address = TestProxy.ServerAddress(server);

        AgentPairingResult result;

        using (HttpMessageHandler handler = Handler(address, proxy.Uri))
        {
            result = await AgentPairing.PairAsync(
                address,
                text,
                PairedFolder.Version,
                handler,
                RequestTimeout,
                server.Clock,
                cancellationToken);
        }

        AgentPairingSettings settings = Assert.IsType<AgentPairingSettings>(result.Settings);
        LoopbackAgent agent = new(settings.Agent);
        AgentSecrets secrets = new(
            Assert.IsType<byte[]>(result.PrivateKey),
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [(TestCalls.BaseKey, OutpostModeEnum.Read)] = "read-pw",
                [(TestCalls.BaseKey, OutpostModeEnum.Write)] = "write-pw",
            });

        agent.Data.Root.Seed("agent.json", AgentPairingFile.Write(settings));
        agent.Data.Root.Seed("secrets.dat", SecretsFile.Write(secrets, agent.Protection));
        agent.Data.Root.Seed("policy.json", Policy(proxy));

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
            TestHost.Executor,
            Handler,
            clock,
            PairedFolder.Version);

        return HostRun.Start(parts);
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

    private static string Policy(TestProxy proxy)
    {
        return $$"""
            {
              "proxy": "{{proxy.Uri}}",
              "bases": [
                {
                  "key": "{{TestCalls.BaseKey}}",
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
