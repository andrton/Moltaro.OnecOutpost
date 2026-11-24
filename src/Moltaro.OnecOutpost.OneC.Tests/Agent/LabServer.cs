using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// The server of the lab's tests of the agent, in the process (the S9b brief, "The lab tests"): the HttpMessageHandler
// that the agent's connection is given, with the real codec and signatures under the server key that agent.json pins.
// It verifies every request with the agent's key and reads it as the server does. It hands a command, signed, once a
// poll declares a free worker of the command's base and mode; answers at once, without a command, a poll that came
// while a command waits for a free worker, so that the agent polls again; holds a poll while no command waits, until
// one comes; and confirms each outcome, which it gives the test. It grants the agent every base it declares, pairs no
// agent, and runs no command of the samples. Its answers carry the poll timeout and the lease grace of agent.json, the
// longest the protocol allows, so that no move of the test's clock ends a held poll. A request it cannot take faults
// every wait of the test, which then fails with the reason. It counts the polls, for the tests of the console's check,
// which never polls.
internal sealed class LabServer : HttpMessageHandler
{
    public static readonly TimeSpan PollTimeout = OutpostProtocol.MaxPollTimeout;

    public static readonly TimeSpan LeaseGrace = OutpostProtocol.MaxLeaseGrace;

    // The take deadline and the skew of a command, the server's defaults; its outcome limit is LabLimits'.
    private static readonly TimeSpan TakeTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    private readonly ECDsa _serverKey;

    private readonly ECDsa _agentKey;

    private readonly Guid _agent;

    private readonly TimeProvider _clock;

    // Faulted with the reason of the first request it could not take; it never completes otherwise.
    private readonly TaskCompletionSource _fault = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Guards every field below it. Nothing waits under it, and nothing is completed under it.
    private readonly Lock _lock = new();

    // The commands that no poll took yet, in the order they came.
    private readonly List<(OutpostCommandMessage Command, OutpostSignedCommand Signed)> _waiting = [];

    // Completed when a command comes, and replaced then, so that a held poll wakes once for it.
    private TaskCompletionSource _came = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _received = [];

    private readonly Dictionary<Guid, TaskCompletionSource<OutpostOutcomeMessage>> _outcomes = [];

    private int _polls;

    // The server's key signs; the agent's key verifies; the clock gives the times of commands and answers.
    public LabServer(ECDsa serverKey, ECDsa agentKey, Guid agent, TimeProvider clock)
    {
        _serverKey = serverKey;
        _agentKey = agentKey;
        _agent = agent;
        _clock = clock;
    }

    // The polls that came so far.
    public int Polls => Volatile.Read(ref _polls);

    // Makes a command of the base and the mode, issued now, signs it and hands it to the next poll that declares a free
    // worker for it. A Write has a caller key of its own unless the test gives one. It gives the command's identity.
    public Guid Send(string baseKey, OutpostModeEnum mode, string script, string parameters = "{}", string? callerKey = null)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        string? key = mode == OutpostModeEnum.Write ? callerKey ?? $"lab-{Guid.NewGuid():N}" : callerKey;

        using JsonDocument document = JsonDocument.Parse(parameters);

        OutpostCommandMessage command = new(
            OutpostProtocol.Version,
            Guid.NewGuid(),
            _agent,
            baseKey,
            mode,
            key,
            script,
            document.RootElement.Clone(),
            now,
            now + TakeTimeout,
            Skew,
            LabLimits.OutcomeLimit);

        byte[] bytes = OutpostCodec.Write(command);
        OutpostSignedCommand signed = new(bytes, OutpostSignatures.SignCommand(_serverKey, bytes));
        TaskCompletionSource came;

        lock (_lock)
        {
            _waiting.Add((command, signed));
            came = _came;
            _came = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        came.SetResult();

        return command.Id;
    }

    // Completes once a poll listed the command as received.
    public Task ReceivedAsync(Guid command)
    {
        return UnlessFaultedAsync(Event(_received, command).Task);
    }

    // Completes with the outcome of the command once the agent sent it, which the server confirmed. A command of another
    // run of the agent, which this server did not make, is read for either mode, as a server reads an outcome of a
    // command it does not know.
    public Task<OutpostOutcomeMessage> OutcomeAsync(Guid command)
    {
        return UnlessFaultedAsync(Event(_outcomes, command).Task);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        string path = request.RequestUri!.AbsolutePath;
        string name = path[(path.LastIndexOf('/') + 1)..];

        if (!OutpostHeaders.TryParseAgent(Header(request, OutpostProtocol.AgentHeader), out Guid agent) || agent != _agent)
        {
            throw Fault($"A request of the agent names another agent, or none, in {OutpostProtocol.AgentHeader}.");
        }

        if (!OutpostHeaders.TryParseSignature(Header(request, OutpostProtocol.SignatureHeader), out byte[]? signature))
        {
            throw Fault($"A request of the agent has no {OutpostProtocol.SignatureHeader} in the form of §2.");
        }

        return name switch
        {
            "poll" => await PollAsync(body, signature, cancellationToken),
            "outcome" => Outcome(body, signature),
            _ => throw Fault($"The agent sent the request {name}, which the lab's server does not take."),
        };
    }

    private static string? Header(HttpRequestMessage request, string name)
    {
        return request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.SingleOrDefault() : null;
    }

    private async Task<HttpResponseMessage> PollAsync(byte[] body, byte[] signature, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _polls);

        OutpostPollRequest poll = Read(() => OutpostCodec.ReadPollRequest(_agentKey, _agent, body, signature));

        foreach (Guid command in poll.Received)
        {
            Event(_received, command).TrySetResult(true);
        }

        Task came;

        lock (_lock)
        {
            came = _waiting.Count == 0 ? _came.Task : Task.CompletedTask;
        }

        // The agent's wait for the answer, or its stop, cancels the token.
        await came.WaitAsync(cancellationToken);

        OutpostPollAnswer answer = new(
            _clock.GetUtcNow(),
            null,
            PollTimeout,
            LeaseGrace,
            [.. poll.Bases.Select(served => served.Key)],
            [],
            Take(poll));

        return Signed(OutpostRequestKindEnum.Poll, body, OutpostCodec.Write(answer));
    }

    // The waiting commands that the poll declares a free worker for, oldest first, each free worker taking one.
    private List<OutpostSignedCommand> Take(OutpostPollRequest poll)
    {
        Dictionary<(string Base, OutpostModeEnum Mode), int> free = [];

        foreach (OutpostPollBase served in poll.Bases)
        {
            free[(served.Key, OutpostModeEnum.Read)] = served.Read.Free;
            free[(served.Key, OutpostModeEnum.Write)] = served.Write?.Free ?? 0;
        }

        List<OutpostSignedCommand> taken = [];

        lock (_lock)
        {
            int index = 0;

            while (index < _waiting.Count)
            {
                (OutpostCommandMessage command, OutpostSignedCommand signed) = _waiting[index];

                if (free.GetValueOrDefault((command.Base, command.Mode)) > 0)
                {
                    free[(command.Base, command.Mode)]--;
                    taken.Add(signed);
                    _waiting.RemoveAt(index);
                }
                else
                {
                    index++;
                }
            }
        }

        return taken;
    }

    private HttpResponseMessage Outcome(byte[] body, byte[] signature)
    {
        OutpostOutcomeRequest request = Read(() =>
        {
            OutpostOutcomeRequest read =
                OutpostCodec.ReadOutcomeRequest(_agentKey, _agent, body, signature, out JsonDocument document);

            document.Dispose();

            return read;
        });
        OutpostOutcomeMessage outcome = Read(() => OutpostCodec.ReadOutcome(null, request.Outcome.Span));

        Event(_outcomes, request.Command).TrySetResult(outcome);

        byte[] confirmation = OutpostCodec.Write(new OutpostOutcomeAnswer(_clock.GetUtcNow(), request.Command));

        return Signed(OutpostRequestKindEnum.Outcome, body, confirmation);
    }

    // The answer 200 with the body, signed with the server's key for the request (§3 of docs/protocol.md).
    private HttpResponseMessage Signed(OutpostRequestKindEnum kind, byte[] requestBody, byte[] answerBody)
    {
        byte[] signature = OutpostSignatures.SignAnswer(_serverKey, kind, _agent, 200, requestBody, answerBody);
        ByteArrayContent content = new(answerBody);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response = new(HttpStatusCode.OK) { Content = content };
        response.Headers.TryAddWithoutValidation(OutpostProtocol.SignatureHeader, OutpostHeaders.FormatSignature(signature));

        return response;
    }

    // What the codec reads, or a fault of the server with the codec's reason.
    private T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (OutpostProtocolException exception)
        {
            throw Fault($"A request of the agent breaks the protocol: {exception.Message}");
        }
    }

    private InvalidOperationException Fault(string reason)
    {
        InvalidOperationException fault = new(reason);
        _fault.TrySetException(fault);

        return fault;
    }

    // The wait, unless the server faulted first, which then fails it with the reason of the fault.
    private async Task<T> UnlessFaultedAsync<T>(Task<T> wait)
    {
        Task first = await Task.WhenAny(wait, _fault.Task);

        if (first == _fault.Task)
        {
            await _fault.Task;
        }

        return await wait;
    }

    // The event of the command, made by whichever comes first: the request or the test that waits for it.
    private TaskCompletionSource<T> Event<T>(Dictionary<Guid, TaskCompletionSource<T>> events, Guid command)
    {
        lock (_lock)
        {
            if (!events.TryGetValue(command, out TaskCompletionSource<T>? reported))
            {
                reported = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                events[command] = reported;
            }

            return reported;
        }
    }
}
