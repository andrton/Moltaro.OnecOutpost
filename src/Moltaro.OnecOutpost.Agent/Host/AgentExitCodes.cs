namespace Moltaro.OnecOutpost.Agent.Host;

// The exit codes of the agent program, which the service reports as ERROR_SERVICE_SPECIFIC_ERROR with the code when it
// is not 0.
internal static class AgentExitCodes
{
    // Stopped by the token, a cancellation while workers open included.
    public const int Stopped = 0;

    // A stop of the agent's own (AgentStopException), recorded when the log can be written.
    public const int Error = 1;

    // The audit log was not opened, a second copy included; nothing recorded.
    public const int NotStarted = 2;

    // The program started with service outside the service controller.
    public const int NotService = 3;

    // Any arguments but service.
    public const int Usage = 4;

    // Not on Windows.
    public const int NotWindows = 5;
}
