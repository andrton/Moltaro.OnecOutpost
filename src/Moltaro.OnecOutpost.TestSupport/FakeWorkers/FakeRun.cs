namespace Moltaro.OnecOutpost.TestSupport.FakeWorkers;

// What one run of a fake worker gives, as the launcher's Runs decides for each command line: the executor's line with
// the bytes of its outcome and their size, which is their length plus SizeOff; the end of the process before any
// outcome, which gives null; or a script that overruns, whose run gives nothing until Kill ends the process.
internal sealed record FakeRun(byte[]? Outcome, long SizeOff, bool WaitsForKill)
{
    // The executor's line: the outcome with its true size, the length of its bytes.
    public static FakeRun Gives(byte[] outcome)
    {
        return new FakeRun(outcome, 0, false);
    }

    // The executor's line with a size that is not the length of its bytes, here by `off`: an outcome the agent cannot
    // accept.
    public static FakeRun GivesSizeOff(byte[] outcome, long off)
    {
        return new FakeRun(outcome, off, false);
    }

    // The process ends, or breaks its line, before it writes an outcome: the run gives null at once.
    public static FakeRun Ends()
    {
        return new FakeRun(null, 0, false);
    }

    // The script overruns: the run gives nothing until Kill ends the process, and then null.
    public static FakeRun Overruns()
    {
        return new FakeRun(null, 0, true);
    }
}
