using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Workers;

// One worker of the console opened in a base, in read mode, for check and metadata (review 3, the runner joined to
// S10c): its open and each of its Reads raced with the run limit on the clock, the worker killed at it, and each
// outcome judged as the pool judges one. A Read after the worker ended is a mistake of the caller. The session owns its
// worker and waits for its exit before closing the job. Its caller runs it from one task at a time.
internal sealed class WorkerSession : IAsyncDisposable
{
    private readonly ConsoleWorker _worker;

    private readonly TimeSpan _runLimit;

    private readonly TimeProvider _clock;

    private readonly CancellationToken _cancellationToken;

    private WorkerSession(ConsoleWorker worker, TimeSpan runLimit, TimeProvider clock, CancellationToken cancellationToken)
    {
        _worker = worker;
        _runLimit = runLimit;
        _clock = clock;
        _cancellationToken = cancellationToken;
    }

    // The worker ended: at the run limit, or as WorkerOutcome.Judge says, after which it runs nothing more.
    public bool Ended { get; private set; }

    // Starts the worker of the program for the base, under the account or the console's own restricted token, and opens
    // it with the connection line within the run limit: the session, or null and the reason, "1C opened no session
    // within {runLimit}, the mode's runLimit" or "1C opened no session: {diagnostics}", the worker closed, its job with
    // it, before its diagnostics are awaited, which then end. A Start that fails passes its Win32Exception or
    // IOException, for the caller to name.
    public static async Task<(WorkerSession? Session, string? Reason)> OpenAsync(
        IWorkerStarts starts,
        IWorkerAccount? account,
        string program,
        string baseKey,
        WorkerConnectionLine connection,
        TimeSpan runLimit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ConsoleWorker worker = starts.Start(account, program, baseKey);
        WorkerSession session = new(worker, runLimit, clock, cancellationToken);
        bool opened = false;
        bool overran;

        try
        {
            (bool ready, overran) = await WithinAsync(
                worker.Process,
                runLimit,
                clock,
                () => worker.Process.OpenAsync(connection, cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();
            opened = ready && !overran;

            if (opened)
            {
                return (session, null);
            }
        }
        finally
        {
            if (!opened)
            {
                await session.DisposeAsync();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (overran)
        {
            return (null, $"1C opened no session within {runLimit:c}, the mode's runLimit");
        }

        string diagnostics = await worker.Diagnostics.WaitAsync(cancellationToken);

        return (null, $"1C opened no session: {DiagnosticsOf(diagnostics)}");
    }

    // One Read of the script with its parameters as JSON, no caller key and OutpostProtocol.MaxOutcomeSizeLimit, within
    // the run limit: the outcome as WorkerOutcome.Judge and ReadOutcome give it, with the size of its bytes; or a null
    // outcome and 0 when it did not end within the run limit, which ended the worker. A Read after Ended is an
    // InvalidOperationException.
    public async Task<(OutpostOutcomeMessage? Outcome, long Size)> ReadAsync(
        string script,
        string parametersJson,
        CancellationToken cancellationToken)
    {
        if (Ended)
        {
            throw new InvalidOperationException("The worker of the session has ended.");
        }

        WorkerCommandLine command = new(script, parametersJson, null, OutpostProtocol.MaxOutcomeSizeLimit);

        ((long Size, byte[] Outcome)? given, bool overran) = await WithinAsync(
            _worker.Process,
            _runLimit,
            _clock,
            () => _worker.Process.RunAsync(command, cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();

        if (overran)
        {
            Ended = true;

            return (null, 0);
        }

        JudgedOutcome judged = WorkerOutcome.Judge(OutpostModeEnum.Read, given);
        Ended = judged.EndsWorker;

        return (OutpostCodec.ReadOutcome(OutpostModeEnum.Read, judged.Outcome), judged.Outcome.Length);
    }

    // Lets the worker exit at the end of its input, or terminates it immediately on cancellation. The job closes only
    // after the exit, so the next worker or the end of the command cannot race the release of 1C and the program files.
    public async ValueTask DisposeAsync()
    {
        IWorkerProcess process = _worker.Process;

        try
        {
            using CancellationTokenRegistration cancel = _cancellationToken.Register(process.Kill);
            await process.EndAsync(_runLimit, _clock);
        }
        finally
        {
            process.Dispose();
        }
    }

    // What the worker wrote to its standard error, which holds no password, or that it wrote nothing.
    private static string DiagnosticsOf(string diagnostics)
    {
        string text = diagnostics.ReplaceLineEndings("\n").Trim();

        return text.Length == 0 ? "the worker gave no reason" : text;
    }

    // Runs a call of the worker raced with the run limit on the clock, the worker killed at it, which ends the call; and
    // whether the limit came. The timer goes with the race.
    private static async Task<(T Result, bool Overran)> WithinAsync<T>(
        IWorkerProcess process,
        TimeSpan runLimit,
        TimeProvider clock,
        Func<Task<T>> call)
    {
        using CancellationTokenSource limit = new(runLimit, clock);
        T result;

        using (limit.Token.Register(process.Kill))
        {
            result = await call();
        }

        return (result, limit.IsCancellationRequested);
    }
}
