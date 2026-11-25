namespace Moltaro.OnecOutpost.Shell;

// Carries a reason the shell can print without exposing a credential from a lower-level exception.
internal sealed class ShellError(int status, string reason) : Exception(reason)
{
    public int Status { get; } = status;
}
