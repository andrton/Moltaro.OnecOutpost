using System.Text.Json.Nodes;

namespace Moltaro.OnecOutpost.Server.Tests.Loopback;

// The calls of the tests over loopback HTTP: a Read or a Write, chosen by the word of its mode in §7 of
// docs/protocol.md, in the base that the test agents declare, with the script and the parameters of the tests unless a
// test names its own. A Write always has a caller key; a Read has the one given, which may be null.
internal static class TestCalls
{
    public const string BaseKey = "accounting";

    public const string Script = "Result = Parameters.Get(\"n\");";

    public const string Parameters = """{"n":1}""";

    public static Task<OutpostOutcome> CallAsync(
        IOutpostServer server,
        string mode,
        string? callerKey,
        CancellationToken cancellationToken)
    {
        return CallAsync(server, mode, callerKey, Script, Parameters, cancellationToken);
    }

    // The parameters are given as JSON text, which the call receives as the JsonObject parsed from it.
    public static Task<OutpostOutcome> CallAsync(
        IOutpostServer server,
        string mode,
        string? callerKey,
        string script,
        string parameters,
        CancellationToken cancellationToken)
    {
        JsonObject written = JsonNode.Parse(parameters)!.AsObject();

        Task<OutpostOutcome> call = mode switch
        {
            "read" => server.ReadAsync(
                new OutpostReadRequest
                {
                    BaseKey = BaseKey,
                    CallerKey = callerKey,
                    Script = script,
                    Parameters = written,
                },
                cancellationToken),
            "write" => server.WriteAsync(
                new OutpostWriteRequest
                {
                    BaseKey = BaseKey,
                    CallerKey = callerKey ?? throw new ArgumentNullException(nameof(callerKey)),
                    Script = script,
                    Parameters = written,
                },
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "A mode is read or write."),
        };

        return WithDeadline(call);
    }

    // A call with the deadline of every wait of a test (LoopbackServer.Deadline), so that a call that never returns
    // fails the test with a TimeoutException instead of hanging it. Its outcome, its exception and its cancellation
    // come as the call's.
    public static Task<OutpostOutcome> WithDeadline(Task<OutpostOutcome> call)
    {
        return call.WaitAsync(LoopbackServer.Deadline);
    }
}
