using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport.Store;
using Moltaro.Signals;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// One node of the server as an application embeds it, reached over loopback HTTP: Kestrel on port 0 of 127.0.0.1, the
// store in a schema of its own that the library migrated, a signing key made for the node, the fake clock as the
// TimeProvider of the options, the endpoints of the agents at /outpost, and an in-process SignalHost as its ISignals,
// or the ISignals that the test gives. A test starts one and drives it through the public API and through test agents,
// or through the agent itself. Another node of the same server, on the same store, with the same signing key, clock and
// options, starts from it, as TwoNodes does. In front of the endpoints, it can play a hop whose body limit refuses an
// outcome with a bare 413 and an HTML page of its own.
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

    // The body limit of the hop in front of the endpoints, shared with the middleware that plays it; null while there
    // is none. A test sets it before it lets the agent send the requests it concerns.
    private readonly StrongBox<long?> _pathLimit;

    // What another node of the same server starts with: the key in PEM and the change of the options.
    private readonly string _signingKeyPem;

    private readonly Action<OutpostServerOptions> _configure;

    private bool _stopped;

    private LoopbackServer(
        WebApplication app,
        ChannelReader<EndedRequest> ended,
        StrongBox<long?> pathLimit,
        string signingKeyPem,
        Action<OutpostServerOptions> configure,
        FakeClock clock,
        MigratedSchema schema,
        OutpostServerOptions options)
    {
        _app = app;
        _ended = ended;
        _pathLimit = pathLimit;
        _signingKeyPem = signingKeyPem;
        _configure = configure;
        Clock = clock;
        Schema = schema;
        Options = options;
        Server = app.Services.GetRequiredService<IOutpostServer>();
        Signals = app.Services.GetRequiredService<ISignals>();
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

    // The ISignals of the node, through which a test watches the keys that the node sets.
    public ISignals Signals { get; }

    // The address of the endpoints, http://127.0.0.1:{port}/outpost/, so that v1/poll relative to it names a request.
    public Uri Address { get; }

    // The client of the test agents.
    public HttpClient Client { get; }

    // When set, a hop in front of the endpoints, as an ingress with a body limit is, answers every outcome request whose
    // declared length is beyond this many bytes with a bare 413 and an HTML page, before the endpoint reads anything
    // (§9 of docs/protocol.md). The other requests pass.
    public long? OutcomeLimitOnThePath
    {
        get => _pathLimit.Value;
        set => _pathLimit.Value = value;
    }

    public static Task<LoopbackServer> StartAsync(
        PostgreSqlFixture postgres,
        string schemaName,
        CancellationToken cancellationToken)
    {
        return StartAsync(postgres, schemaName, _ => { }, cancellationToken);
    }

    // Starts a node whose options the test may change after the store, the signing key and the clock are set.
    public static Task<LoopbackServer> StartAsync(
        PostgreSqlFixture postgres,
        string schemaName,
        Action<OutpostServerOptions> configure,
        CancellationToken cancellationToken)
    {
        return StartNewAsync(postgres, schemaName, configure, null, cancellationToken);
    }

    // Starts a node that reaches the Signals host through the ISignals given, such as the client of RemoteSignals, which
    // the test owns and disposes after the node.
    public static Task<LoopbackServer> StartAsync(
        PostgreSqlFixture postgres,
        string schemaName,
        Action<OutpostServerOptions> configure,
        ISignals signals,
        CancellationToken cancellationToken)
    {
        return StartNewAsync(postgres, schemaName, configure, signals, cancellationToken);
    }

    // Starts another node of the same server as this one, which may have stopped: on its store, with its signing key,
    // its clock and the same change of the options, at a port of its own, reaching the Signals host through the ISignals
    // given, which the test owns and disposes after the node. So a second node, and a node started again after a stop.
    public Task<LoopbackServer> StartAnotherAsync(ISignals signals, CancellationToken cancellationToken)
    {
        return StartNodeAsync(Schema, Clock, _signingKeyPem, _configure, signals, cancellationToken);
    }

    private static async Task<LoopbackServer> StartNewAsync(
        PostgreSqlFixture postgres,
        string schemaName,
        Action<OutpostServerOptions> configure,
        ISignals? signals,
        CancellationToken cancellationToken)
    {
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, schemaName, cancellationToken);

        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string signingKeyPem = signingKey.ExportPkcs8PrivateKeyPem();

        return await StartNodeAsync(schema, new FakeClock(), signingKeyPem, configure, signals, cancellationToken);
    }

    private static async Task<LoopbackServer> StartNodeAsync(
        MigratedSchema schema,
        FakeClock clock,
        string signingKeyPem,
        Action<OutpostServerOptions> configure,
        ISignals? signals,
        CancellationToken cancellationToken)
    {
        OutpostServerOptions? options = null;

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        if (signals is null)
        {
            InProcessSignals.Add(builder.Services);
        }
        else
        {
            builder.Services.AddSingleton(signals);
        }

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
        StrongBox<long?> pathLimit = new(null);

        try
        {
            app.Use(async (context, next) =>
            {
                // A header is read, never the body, which only the endpoint reads within its limit.
                string? signature = context.Request.Headers[OutpostProtocol.SignatureHeader].FirstOrDefault();

                try
                {
                    await next(context);
                }
                finally
                {
                    ended.Writer.TryWrite(
                        new EndedRequest(context.Request.Path, context.Response.HasStarted, signature));
                }
            });

            app.Use(async (context, next) =>
            {
                bool refused = pathLimit.Value is long limit
                    && context.Request.Path.Value!.EndsWith("/v1/outcome", StringComparison.Ordinal)
                    && context.Request.ContentLength > limit;

                if (!refused)
                {
                    await next(context);

                    return;
                }

                // The body is not read, so Kestrel sends no 100 Continue, as a hop that refuses a declared length.
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(
                    "<html><head><title>413 Request Entity Too Large</title></head><body>Request Entity Too Large</body></html>");
            });

            app.MapOutpostAgents(Prefix);
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return new LoopbackServer(app, ended.Reader, pathLimit, signingKeyPem, configure, clock, schema, options!);
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

    // Completes once `count` timers of the clock are armed to fire at or before dueBy: for calls, one timer for the
    // wait of each, which ends it at the earlier of its due moment and PollTimeout after it starts.
    public Task WaitForTimersAsync(DateTimeOffset dueBy, int count, CancellationToken cancellationToken)
    {
        return Clock.WaitForTimersAsync(dueBy, count, cancellationToken).WaitAsync(Deadline, cancellationToken);
    }

    // The key of the signals for an agent or a command, as the node names it: the prefix of the options, then the
    // identity in the form of §2 of docs/protocol.md.
    public string SignalKey(Guid id)
    {
        return Options.SignalKeyPrefix + "." + id.ToString("D");
    }

    // Sends a poll that becomes the newest and is held, and gives the task of its answer once the node has recorded it
    // and holds it: once the timer of its hold is armed. No other timer may be due by the end of its hold; a test gives
    // a poll a shorter poll timeout where one is.
    public async Task<Task<AgentAnswer>> HoldPollAsync(
        TestAgent agent,
        OutpostPollRequest poll,
        CancellationToken cancellationToken)
    {
        TimeSpan hold = poll.PollTimeout < Options.PollTimeout ? poll.PollTimeout : Options.PollTimeout;
        DateTimeOffset start = Clock.GetUtcNow();

        Task<AgentAnswer> answer = agent.PollAsync(poll, cancellationToken);
        await WaitForTimerAsync(start + hold, cancellationToken);

        return answer;
    }

    // Starts a call for a base of the agent and gives its task once the node has created its command. The node sets the
    // agent's key after the commit of every command it creates, and a member of a group of its own on that key sees
    // the set. A call that ends before, as one that creates no command does, fails the test.
    public async Task<Task<OutpostOutcome>> StartCallAsync(
        Guid agentId,
        Func<Task<OutpostOutcome>> call,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        using ISignalSubscription created = Signals.Subscribe(SignalKey(agentId), Guid.NewGuid().ToString("N"));

        // The first wait of a member returns Joined once the member is open on its host; a set from then on reaches it.
        Assert.Equal(SignalWaitResultEnum.Joined, await created.WaitAsync(null, deadline.Token));

        Task<OutpostOutcome> outcome = call();
        Task<SignalWaitResultEnum> signaled = created.WaitAsync(null, deadline.Token).AsTask();

        if (await Task.WhenAny(signaled, outcome) == outcome && !signaled.IsCompleted)
        {
            OutpostOutcome ended = await outcome;
            Assert.Fail($"The call ended as {ended.Kind} {ended.Code} before it created a command.");
        }

        Assert.Equal(SignalWaitResultEnum.Signaled, await signaled);

        return outcome;
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

    // Completes once the node has finished handling the request that the answer answers, found by its signature, after
    // the ended requests a test read before. An answer can reach the agent before its endpoint has returned, so this
    // is what tells that the endpoint has let go of what it held, such as the timer of a hold.
    public async Task WaitForEndOfAsync(AgentAnswer answer, CancellationToken cancellationToken)
    {
        string signature = OutpostHeaders.FormatSignature(answer.RequestSignature);

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        EndedRequest ended;

        do
        {
            ended = await _ended.ReadAsync(deadline.Token);
        }
        while (ended.Signature != signature);
    }

    // Holds the poll, makes the call, and gives the task of the call with the command that the poll hands out, the one
    // command of its answer, once the request of the poll has ended on the node: the timer of its hold is then gone,
    // and the next timer that the node arms is the wait of the call.
    public async Task<(Task<OutpostOutcome> Call, OutpostCommandMessage Command)> HandOutAsync(
        TestAgent agent,
        OutpostPollRequest poll,
        Func<Task<OutpostOutcome>> call,
        CancellationToken cancellationToken)
    {
        Task<AgentAnswer> held = await HoldPollAsync(agent, poll, cancellationToken);
        Task<OutpostOutcome> outcome = call();

        AgentAnswer answer = await held;
        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(agent.ReadPollAnswer(poll, answer)));
        await WaitForEndOfAsync(answer, cancellationToken);

        return (outcome, command);
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
