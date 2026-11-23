using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Pair;

// pair <SERVER> <TOKEN> (the S10 brief, "pair", and §6.1 of docs/protocol.md), after the steps it shares with secret,
// against the stub of the pair endpoint: agent.json that exists ends it with AlreadyPaired before anything is sent; then
// AgentPairing.PairAsync with the console's version and the handler of the server through the policy's proxy; a
// verified 200 writes secrets.dat with the new key alone, replacing one that a failed pairing left, and then agent.json,
// each by Data.Replace and then owned by Administrators (Q14), so that agent.json exists only with its key. A file that
// cannot be written then ends pair with Failure and the remedy, the server having paired the agent.
public sealed class PairTests : IDisposable
{
    private readonly TestMachine _machine = new();

    private readonly PairingServer _server;

    public PairTests()
    {
        _machine.Installed();
        _server = new PairingServer(_machine);
    }

    private static string SecretsPath => Path.Combine(TestMachine.DataFolder, "secrets.dat");

    private static string PairingPath => Path.Combine(TestMachine.DataFolder, "agent.json");

    public void Dispose()
    {
        _server.Dispose();
    }

    [Fact]
    public async Task A_verified_200_writes_secrets_dat_with_the_new_key_alone_and_then_agent_json_each_owned_by_Administrators()
    {
        Task<CommandAppResult> run = StartPair();
        ServerExchange exchange = await _server.NextAsync();
        OutpostPairRequest request = PairingServer.Read(exchange);
        exchange.AnswerSigned(200, _server.Answer(request.Key));
        CommandAppResult result = await TestTimes.EndOf(run);

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal(
            ["others \"\"", "request pair", "replace secrets.dat", $"own {SecretsPath}", "replace agent.json", $"own {PairingPath}"],
            _machine.Operations);
        Assert.Contains(
            $"Paired with {PairingServer.Address} as {PairingServer.Agent}; write the passwords with secret.",
            TestMachine.LinesOf(result));

        AgentPairingSettings pairing = AgentPairingFile.Read(_machine.Data.Root.Bytes("agent.json"));
        Assert.Equal(PairingServer.Address, pairing.Server);
        Assert.Equal(PairingServer.Agent, pairing.Agent);
        Assert.Equal(_server.ServerKey, pairing.ServerKey);
        Assert.Equal(PairingServer.PollTimeout, pairing.PollTimeout);
        Assert.Equal(PairingServer.LeaseGrace, pairing.LeaseGrace);

        AgentSecrets secrets = SecretsFile.Read(_machine.Data.Root.Bytes("secrets.dat"), _machine.Protection);
        Assert.Equal(request.Key, PublicKeyOf(secrets.PrivateKey));
        Assert.Empty(secrets.Passwords);
        Assert.Empty(secrets.WorkerPasswords);
    }

    // The proof binds the key sent to the token's secret, so the console paired with the token it was given.
    [Fact]
    public async Task Pair_sends_the_version_of_the_console_with_the_proof_of_the_token_through_the_proxy_of_the_policy()
    {
        Task<CommandAppResult> run = StartPair();
        ServerExchange exchange = await _server.NextAsync();
        OutpostPairRequest request = PairingServer.Read(exchange);
        exchange.AnswerSigned(200, _server.Answer(request.Key));
        await TestTimes.EndOf(run);

        Assert.Equal(TestMachine.Version, request.AgentVersion);
        Assert.True(OutpostPairingProof.Check(_server.Secret, PairingServer.Agent, request.Key, request.Proof));
        Assert.Equal([(PairingServer.Address, new Uri("http://proxy.example.com:3128"))], _machine.Routes);
    }

    // As after a pairing whose agent.json could not be written: secrets.dat holds the key of that pairing and passwords
    // written since, and agent.json is absent.
    [Fact]
    public async Task Pairing_replaces_a_secrets_dat_that_a_failed_pairing_left_with_the_new_key_alone()
    {
        using ECDsa earlier = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        AgentSecrets left = new(
            earlier.ExportPkcs8PrivateKey(),
            new Dictionary<(string Base, OutpostModeEnum Mode), string> { [("accounting", OutpostModeEnum.Read)] = "read-pw" },
            new Dictionary<string, string> { ["accounting"] = "worker-pw" });
        _machine.Data.Root.Seed("secrets.dat", SecretsFile.Write(left, _machine.Protection));

        Task<CommandAppResult> run = StartPair();
        ServerExchange exchange = await _server.NextAsync();
        OutpostPairRequest request = PairingServer.Read(exchange);
        exchange.AnswerSigned(200, _server.Answer(request.Key));
        CommandAppResult result = await TestTimes.EndOf(run);

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        AgentSecrets secrets = SecretsFile.Read(_machine.Data.Root.Bytes("secrets.dat"), _machine.Protection);
        Assert.Equal(request.Key, PublicKeyOf(secrets.PrivateKey));
        Assert.Empty(secrets.Passwords);
        Assert.Empty(secrets.WorkerPasswords);
    }

    [Fact]
    public async Task An_agent_that_is_paired_ends_pair_with_AlreadyPaired_before_anything_is_sent()
    {
        _machine.Data.Root.Seed("agent.json", "{}");

        CommandAppResult result = await _machine.RunAsync("pair", PairingServer.Address.ToString(), _server.Token);

        Assert.Equal(ConsoleExitCodes.AlreadyPaired, result.ExitCode);
        Assert.Contains(
            "AlreadyPaired (11): the agent is paired; to pair it anew, remove agent.json; what its outbox holds then reaches "
            + "the server under the new identity, which drops it as another agent's (§6.3); pairing anew writes secrets.dat "
            + "with the new key alone, so the passwords are written again with secret",
            TestMachine.LinesOf(result));
        Assert.Equal(["others \"\""], _machine.Operations);
        Assert.Empty(_machine.Routes);
    }

    // What the first file holds is written before the second: a secrets.dat that fails leaves no agent.json, and an
    // agent.json that fails leaves the agent unpaired on its machine, its new key in secrets.dat.
    [Theory]
    [InlineData("secrets.dat")]
    [InlineData("agent.json")]
    public async Task A_file_that_cannot_be_written_after_the_verified_200_ends_pair_with_Failure_and_the_remedy(string file)
    {
        _machine.Data.Root.FailReplace(file);

        Task<CommandAppResult> run = StartPair();
        ServerExchange exchange = await _server.NextAsync();
        exchange.AnswerSigned(200, _server.Answer(PairingServer.Read(exchange).Key));
        CommandAppResult result = await TestTimes.EndOf(run);

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains(
            $"Failure (1): the server paired the agent, but {file} could not be written: The test failed the replace of {file}.",
            result.Output);
        Assert.Contains("; the application revokes it and gives a new token", result.Output);
        Assert.False(_machine.Data.Root.Has("agent.json"));
        Assert.Equal(
            file == "secrets.dat"
                ? ["others \"\"", "request pair"]
                : ["others \"\"", "request pair", "replace secrets.dat", $"own {SecretsPath}"],
            _machine.Operations);
    }

    private static byte[] PublicKeyOf(byte[] privateKey)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out _);

        return OutpostKeys.ExportPublicKey(key);
    }

    private Task<CommandAppResult> StartPair()
    {
        return _machine.Start(TestContext.Current.CancellationToken, "pair", PairingServer.Address.ToString(), _server.Token);
    }
}
