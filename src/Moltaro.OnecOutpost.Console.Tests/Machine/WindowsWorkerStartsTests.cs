using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Principal;
using Moltaro.OnecOutpost.Console.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// The probe of check on Windows (the S10b brief, "The probe", and "Tests with Requires=Windows"), with
// Requires=Administrator, since CreateProcessWithTokenW needs "Impersonate a client after authentication", which only an
// elevated token holds: an account of the process's own token, which WindowsWorkerStarts.Account takes in the place of a
// logon, so that no local user is made; the test program, which stands in for the COM worker, started on the account's
// desktop with its input closed before it resumes, ends at the end of its input with exit code 0, and a program that is
// missing fails the call with 2, ERROR_FILE_NOT_FOUND.
[Trait("Requires", "Windows")]
[Trait("Requires", "Administrator")]
[SupportedOSPlatform("windows")]
public sealed class WindowsWorkerStartsTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(1);

    private static string TestProgram => Path.Combine(AppContext.BaseDirectory, "Moltaro.OnecOutpost.TestWorker.exe");

    [Fact]
    public async Task The_probe_under_the_account_of_the_own_token_ends_at_the_end_of_its_input_with_exit_code_0()
    {
        WindowsWorkerStarts starts = new(TimeProvider.System);
        using IWorkerAccount account = OwnAccount(starts);

        uint? exitCode = await TestTimes.EndOf(
            starts.ProbeAsync(account, TestProgram, "accounting", Deadline, TestContext.Current.CancellationToken));

        Assert.Equal(0u, exitCode);
    }

    [Fact]
    public async Task The_probe_of_a_program_that_is_missing_fails_with_the_Windows_error_2()
    {
        WindowsWorkerStarts starts = new(TimeProvider.System);
        using IWorkerAccount account = OwnAccount(starts);
        string missing = Path.Combine(AppContext.BaseDirectory, $"missing-{Guid.NewGuid():N}", "Moltaro.OnecOutpost.TestWorker.exe");

        Win32Exception exception = await Assert.ThrowsAsync<Win32Exception>(() =>
            TestTimes.EndOf(starts.ProbeAsync(account, missing, "accounting", Deadline, TestContext.Current.CancellationToken)));

        Assert.Equal(2, exception.NativeErrorCode);
    }

    // The account of the process's own primary token, with the rights that CreateProcessWithTokenW needs of it, which the
    // account then owns: the identity is left to it undisposed, since a second dispose of the token changes nothing.
    private static IWorkerAccount OwnAccount(WindowsWorkerStarts starts)
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent(
            TokenAccessLevels.AssignPrimary | TokenAccessLevels.Duplicate | TokenAccessLevels.Query);

        return starts.Account("accounting", identity.AccessToken);
    }
}
