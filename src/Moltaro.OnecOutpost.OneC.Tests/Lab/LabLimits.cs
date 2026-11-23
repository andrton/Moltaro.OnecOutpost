namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// The limits that the lab's tests of the worker and of the agent share.
internal static class LabLimits
{
    // How long a test waits for an event of 1C, such as an open, a line of the worker, an outcome or the end of a
    // worker: the first connection of a Windows account that never used 1C took twenty seconds on the lab machine
    // (concept §12).
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    // The outcome limit of a command when a test gives none: the default of the server, 16 MiB (concept rule 5).
    public const int OutcomeLimit = 16 * 1024 * 1024;
}
