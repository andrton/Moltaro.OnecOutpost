using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Server.Tests.Loopback;

// One node of the server as an application embeds it, reached over loopback HTTP: Kestrel on port 0 of 127.0.0.1, the
// store in a schema of its own that the library migrated, a signing key made for the node, the fake clock as the
// TimeProvider of the options, and the endpoints of the agents at /outpost. A test starts one and drives it through
// the public API and through test agents; S5 reuses it.
//
// Every wait of a test on the node has a deadline, reached only when the node never does what the test waits for: on
// loopback, every answer, every timer and every end of a request comes well within it. A test that reaches it fails
// instead of hanging.
internal sealed class LoopbackServer : IAsyncDisposable
{
    public const string Prefix = "/outpost";

    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private readonly WebApplication _app;

    // The requests of the agents that the node finished handling, in the order they ended, written by a middleware
    // around the endpoints and read by the tests that wait for the end of a request.
    private readonly ChannelReader<EndedRequest> _ended;

    private bool _stopped;

    private LoopbackServer(
        WebApplication app,
        ChannelReader<EndedRequest> ended,
        FakeClock clock,
        MigratedSchema schema,
        OutpostServerOptions options)
    {
        _app = app;
        _ended = ended;
        Clock = clock;
        Schema = schema;
        Options = options;
        Server = app.Services.GetRequiredService<IOutpostServer>();
        Address = new Uri(app.Urls.Single() + Prefix + "/");

        // A body is sent with Expect: 100-continue only when the server asks for it: the wait for 100 Continue is
        // longer than every wait of a test.
        SocketsHttpHandler handler = new() { UseProxy = false, Expect100ContinueTimeout = 2 * Deadline };
        Client = new HttpClient(handler) { BaseAddress = Address, Timeout = Deadline };
    }

    public FakeClock Clock { get; }

    // The schema of the node's store, for the tests that read what the store holds.
    public MigratedSchema Schema { get; }

    // The options as the test set them; the server keeps a copy of its own.
    public OutpostServerOptions Options { get; }

    public IOutpostServer Server { get; }

    // The address of the endpoints, http://127.0.0.1:{port}/outpost/, so that v1/poll relative to it names a request.
    public Uri Address { get; }

    // The client of the test agents.
    public HttpClient Client { get; }

    public static Task<LoopbackServer> StartAsync(
        PostgreSqlFixture postgres,
        string schemaName,
        CancellationToken cancellationToken)
    {
        return StartAsync(postgres, schemaName, _ => { }, cancellationToken);
    }

    // Starts a node whose options the test may change after the store, the signing key and the clock are set.
    public static async Task<LoopbackServer> StartAsync(
        PostgreSqlFixture postgres,
        string schemaName,
        Action<OutpostServerOptions> configure,
        CancellationToken cancellationToken)
    {
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, schemaName, cancellationToken);
        FakeClock clock = new();
        OutpostServerOptions? options = null;

        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string signingKeyPem = signingKey.ExportPkcs8PrivateKeyPem();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        ServerSetup.AddSignals(builder.Services);

        builder.Services.AddOutpostServer(configured =>
        {
            configured.ConnectionString = schema.ConnectionString;
            configured.Schema = schema.Name;
            configured.SigningKeyPem = signingKeyPem;
            configured.TimeProvider = clock;
            configure(configured);
            options = configured;
        });

        WebApplication app = builder.Build();
        Channel<EndedRequest> ended = Channel.CreateUnbounded<EndedRequest>();

        try
        {
            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                finally
                {
                    ended.Writer.TryWrite(new EndedRequest(context.Request.Path, context.Response.HasStarted));
                }
            });

            app.MapOutpostAgents(Prefix);
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return new LoopbackServer(app, ended.Reader, clock, schema, options!);
    }

    // A new agent with a pairing token of its own, not paired yet.
    public async Task<TestAgent> CreateAgentAsync(CancellationToken cancellationToken)
    {
        OutpostPairingToken token = await Server.CreatePairingTokenAsync("Test agent", cancellationToken);

        return new TestAgent(Client, Clock, token);
    }

    // A new agent that paired over HTTP with a token of its own and pinned the server key.
    public async Task<TestAgent> PairAgentAsync(CancellationToken cancellationToken)
    {
        TestAgent agent = await CreateAgentAsync(cancellationToken);
        await agent.PairAsync(cancellationToken);

        return agent;
    }

    // Completes once a timer of the clock is armed to fire at or before dueBy: for a poll, the timer of its hold, which
    // the node arms once the poll is recorded; for the purge, the delay of its next round.
    public Task WaitForTimerAsync(DateTimeOffset dueBy, CancellationToken cancellationToken)
    {
        return Clock.WaitForTimerAsync(dueBy, cancellationToken).WaitAsync(Deadline, cancellationToken);
    }

    // Sends the agent's next poll, with the option's poll timeout and the bases given, ends its hold and gives the
    // verified answer. It waits until the timer of the hold is armed before it moves the clock to the end of the hold,
    // so it serves only a poll that becomes the newest.
    public async Task<OutpostPollAnswer> PollToTheEndOfItsHoldAsync(
        TestAgent agent,
        IReadOnlyList<OutpostPollBase> bases,
        CancellationToken cancellationToken)
    {
        OutpostPollRequest poll = agent.NextPoll(Options.PollTimeout, bases);
        DateTimeOffset start = Clock.GetUtcNow();

        Task<AgentAnswer> answer = agent.PollAsync(poll, cancellationToken);
        await WaitForTimerAsync(start + Options.PollTimeout, cancellationToken);
        Clock.Advance(Options.PollTimeout);

        return agent.ReadPollAnswer(poll, await answer);
    }

    // The next request with this name, pair, poll or outcome, that the node finished handling, after those a test read
    // before.
    public async Task<EndedRequest> WaitForEndedRequestAsync(string request, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        while (true)
        {
            EndedRequest ended = await _ended.ReadAsync(deadline.Token);

            if (ended.Path.EndsWith("/" + request, StringComparison.Ordinal))
            {
                return ended;
            }
        }
    }

    // Whether the agent shows as connected in the list of the public API, at the moment of the call.
    public async Task<bool> IsConnectedAsync(Guid agentId, CancellationToken cancellationToken)
    {
        IReadOnlyList<OutpostAgent> agents = await Server.GetAgentsAsync(cancellationToken);

        return agents.Single(agent => agent.Id == agentId).Connected;
    }

    // Stops the node as its host stops: the application's stopping token is cancelled first, then the server.
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopped = true;
        await _app.StopAsync(cancellationToken);
    }

    // The client goes first, so that a request a failed test left open ends and does not hold up the stop.
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        if (!_stopped)
        {
            await _app.StopAsync();
        }

        await _app.DisposeAsync();
    }
}
