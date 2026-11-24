namespace Moltaro.OnecOutpost.Console.Commands;

// The exit codes of the console, as the S10 brief's table gives them, each with the commands that give it.
internal static class ConsoleExitCodes
{
    // Every command: done; --help too.
    public const int Success = 0;

    // Every command: a step failed for a reason of Windows or of the disk, or an unexpected exception.
    public const int Failure = 1;

    // Every command: an unknown command or option, a missing or invalid argument or password, install run from inside
    // the program folder or from a folder that holds it.
    public const int Usage = 2;

    // Every command: the console is not run as administrator; nothing was changed.
    public const int NotElevated = 3;

    // Every command: not on Windows.
    public const int NotWindows = 4;

    // Every command: Ctrl+C, between two steps.
    public const int Cancelled = 5;

    // uninstall, pair, secret and check: the service, or the data folder, does not exist.
    public const int NotInstalled = 6;

    // install and uninstall: the service did not stop within --stop-timeout; nothing further was done.
    public const int StopTimedOut = 7;

    // install: someone else may change the program folder.
    public const int ProgramFolderWriters = 8;

    // install, pair and secret: the data folder admits someone else, or is a reparse point.
    public const int DataFolderAdmitsOthers = 9;

    // pair and secret: policy.json is absent, unreadable or invalid.
    public const int PolicyInvalid = 10;

    // pair: agent.json exists.
    public const int AlreadyPaired = 11;

    // pair: anything but a verified 200.
    public const int PairingFailed = 12;

    // secret: secrets.dat is absent.
    public const int NotPaired = 13;

    // secret: the policy names no such base, mode or worker account.
    public const int NotInPolicy = 14;

    // secret: the worker account is not a local user.
    public const int UnknownAccount = 15;

    // secret: the input is the console and the output is redirected, so no password can be asked.
    public const int NotInteractive = 16;

    // connector register: no bitness ends with a registered connector, several being installed or none.
    public const int ConnectorNotRegistered = 17;

    // connector register: a run of regsvr32 failed, or HKLM shows no registration after it.
    public const int RegistrationFailed = 18;

    // check: a line failed, or was not checked since a line it needs failed.
    public const int CheckFailed = 19;
}
