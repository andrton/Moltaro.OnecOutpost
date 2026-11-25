using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.FakeHttp;

// The fake server supplies the agent's HTTP handler (AgentParts.Handlers) and the console's stub pairing endpoint
// (ConsoleParts.Handlers). It hands each request to the test as a ServerExchange,
// queued by kind in the order the requests came, and answers with what the test gives: answers and commands signed
// with the server's key as the server signs them (§3 of docs/protocol.md), and anything else that a server or a hop on
// the path could send. It never answers by itself: a request the test leaves alone waits until the
// agent gives up on it. When it is given a data folder in memory, each request is a line "request pair",
// "request poll" or "request outcome" of its operation list, so that a test asserts the order of the requests among
// the writes of the audit log and the outbox.
internal sealed class FakeServer : HttpMessageHandler
{
    // The poll timeout and the lease grace that the answers of the tests carry unless a test gives others: the options'
    // defaults, as agent.json of PairedFolder holds them.
    public static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(25);

    public static readonly TimeSpan LeaseGrace = TimeSpan.FromSeconds(10);

    private readonly ECDsa _serverKey;

    private readonly TimeProvider _clock;

    private readonly MemoryDataFolder? _notes;

    private readonly Dictionary<OutpostRequestKindEnum, Channel<ServerExchange>> _queues = new()
    {
        [OutpostRequestKindEnum.Pair] = Channel.CreateUnbounded<ServerExchange>(),
        [OutpostRequestKindEnum.Poll] = Channel.CreateUnbounded<ServerExchange>(),
        [OutpostRequestKindEnum.Outcome] = Channel.CreateUnbounded<ServerExchange>(),
    };

    // Guards the counts below. Nothing waits under it.
    private readonly Lock _lock = new();

    private readonly Dictionary<OutpostRequestKindEnum, int> _counts = [];

    private int _outcomesOpen;

    private int _mostOutcomesOpen;

    private bool _disposed;

    // The server's key signs; the clock gives the time of the answers that the server makes.
    public FakeServer(ECDsa serverKey, TimeProvider clock, MemoryDataFolder? notes = null)
    {
        _serverKey = serverKey;
        _clock = clock;
        _notes = notes;
    }

    // The agent's key, which verifies the polls and the outcomes the agent sends, so that a test reads them; null for a
    // test that reads none.
    public ECDsa? AgentKey { get; init; }

    // Runs when the handler is disposed, before it counts as disposed: a test that states what its owner disposed
    // before it looks there.
    public Action? Disposing { get; set; }

    public bool IsDisposed
    {
        get
        {
            lock (_lock)
            {
                return _disposed;
            }
        }
    }

    // The most outcome requests that were waiting for their answers at once.
    public int MostOutcomesAtOnce
    {
        get
        {
            lock (_lock)
            {
                return _mostOutcomesOpen;
            }
        }
    }

    // The requests of the kind that came so far.
    public int Count(OutpostRequestKindEnum kind)
    {
        lock (_lock)
        {
            return _counts.GetValueOrDefault(kind);
        }
    }

    // The next request of the kind, after those a test took before; it fails the test when none comes within the
    // deadline of the tests.
    public async Task<ServerExchange> NextAsync(OutpostRequestKindEnum kind)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return await _queues[kind].Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    public Task<ServerExchange> NextPollAsync()
    {
        return NextAsync(OutpostRequestKindEnum.Poll);
    }

    public Task<ServerExchange> NextOutcomeAsync()
    {
        return NextAsync(OutpostRequestKindEnum.Outcome);
    }

    // The command as the server's writer writes it, signed with the server's key (§3, §7).
    public OutpostSignedCommand Sign(OutpostCommandMessage command)
    {
        return Sign(OutpostCodec.Write(command));
    }

    // Bytes written by hand, which no server writes, signed with the server's key.
    public OutpostSignedCommand Sign(string bytes)
    {
        return Sign(Encoding.UTF8.GetBytes(bytes));
    }

    // The bytes of a command signed with a key that is not the server's.
    public static OutpostSignedCommand Forge(OutpostCommandMessage command)
    {
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] bytes = OutpostCodec.Write(command);

        return new OutpostSignedCommand(bytes, OutpostSignatures.SignCommand(other, bytes));
    }

    // The ordinary answer to a poll (§6.2), at the time of the clock, with the commands given and no bases.
    public OutpostPollAnswer Ordinary(params OutpostSignedCommand[] commands)
    {
        return new OutpostPollAnswer(_clock.GetUtcNow(), null, PollTimeout, LeaseGrace, [], [], commands);
    }

    // The answer to an older poll (§6.2): at once, without commands, naming the newest number the server recorded.
    public OutpostPollAnswer Older(long newest)
    {
        return new OutpostPollAnswer(_clock.GetUtcNow(), newest, PollTimeout, LeaseGrace, [], [], []);
    }

    // The signature of an answer of this server to the request (§3).
    internal byte[] SignAnswer(ServerExchange exchange, int status, byte[] body)
    {
        return OutpostSignatures.SignAnswer(_serverKey, exchange.Kind, exchange.Agent, status, exchange.Body, body);
    }

    internal DateTimeOffset Now()
    {
        return _clock.GetUtcNow();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        ServerExchange exchange = new(this, request, body);

        lock (_lock)
        {
            _counts[exchange.Kind] = _counts.GetValueOrDefault(exchange.Kind) + 1;

            if (exchange.Kind == OutpostRequestKindEnum.Outcome)
            {
                _outcomesOpen++;
                _mostOutcomesOpen = Math.Max(_mostOutcomesOpen, _outcomesOpen);
            }
        }

        _notes?.Note($"request {exchange.Name}");
        _queues[exchange.Kind].Writer.TryWrite(exchange);

        try
        {
            // The agent's wait ends this one: its deadline or its stop cancels the token.
            return await exchange.Answered.WaitAsync(cancellationToken);
        }
        finally
        {
            if (exchange.Kind == OutpostRequestKindEnum.Outcome)
            {
                lock (_lock)
                {
                    _outcomesOpen--;
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        Disposing?.Invoke();

        lock (_lock)
        {
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private OutpostSignedCommand Sign(byte[] bytes)
    {
        return new OutpostSignedCommand(bytes, OutpostSignatures.SignCommand(_serverKey, bytes));
    }
}
