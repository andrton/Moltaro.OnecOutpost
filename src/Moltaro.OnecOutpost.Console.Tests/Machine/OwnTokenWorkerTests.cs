using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// A worker of check under the console's own token on Windows (the S10b brief, "The connections"), with Requires=Windows
// alone, since CreateProcessAsUserW needs no privilege for the caller's own token: the test program, which stands in
// for the COM worker, starts with the base key and read as its command line, in a job of its own, which holds it and
// not the console, speaks the worker's lines, and what it writes to its standard error is the worker's Diagnostics,
// read to its end once the worker was disposed. Its token is a restricted version of the console's own (the contract
// amended after the first code review), so that the code of a base does not run as an administrator: Administrators,
// when the console's token holds it, for deny only, and no privilege but SeChangeNotifyPrivilege.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed partial class OwnTokenWorkerTests
{
    private const string Text = "the diagnostics of the test program";

    // PROCESS_QUERY_LIMITED_INFORMATION and TOKEN_QUERY, to read the token of the worker.
    private const uint QueryLimitedInformation = 0x1000;

    private const uint TokenQuery = 0x0008;

    // SE_GROUP_ENABLED and SE_GROUP_USE_FOR_DENY_ONLY.
    private const uint Enabled = 0x4;

    private const uint DenyOnly = 0x10;

    private static string TestProgram => Path.Combine(AppContext.BaseDirectory, "Moltaro.OnecOutpost.TestWorker.exe");

    [Fact]
    public async Task A_worker_under_the_console_own_token_runs_in_read_mode_in_a_job_of_its_own_and_its_standard_error_is_its_diagnostics()
    {
        CheckWorker worker = new WindowsWorkerStarts(TimeProvider.System).Start(null, TestProgram, "accounting");
        JsonElement arguments;
        JsonElement pid;
        JsonElement job;

        using (worker.Process)
        {
            Assert.True(await TestTimes.EndOf(worker.Process.OpenAsync(Connection(), TestContext.Current.CancellationToken)));

            arguments = await ResultOfAsync(worker, "arguments");
            pid = await ResultOfAsync(worker, "pid");
            job = await ResultOfAsync(worker, "job");
            Assert.Equal(JsonValueKind.Null, (await ResultOfAsync(worker, $"diagnostics {Text}")).ValueKind);
        }

        Assert.Equal(["accounting", "read"], arguments.EnumerateArray().Select(argument => argument.GetString()));

        // The console host of the worker joins its job as well, so the job may hold more than the worker.
        List<int> inJob = [.. job.EnumerateArray().Select(id => id.GetInt32())];
        Assert.Contains(pid.GetInt32(), inJob);
        Assert.DoesNotContain(Environment.ProcessId, inJob);
        Assert.Equal(Text + "\n", await TestTimes.EndOf(worker.Diagnostics));
    }

    // CreateRestrictedToken with DISABLE_MAX_PRIVILEGE and Administrators deny-only: an elevated console holds
    // Administrators and the privileges of an administrator, a console that is not elevated holds Administrators for
    // deny only already, or not at all, and privileges of its own beside SeChangeNotifyPrivilege.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_worker_under_the_console_own_token_holds_Administrators_for_deny_only_and_no_privilege_but_change_notify()
    {
        CheckWorker worker = new WindowsWorkerStarts(TimeProvider.System).Start(null, TestProgram, "accounting");
        List<(SecurityIdentifier Sid, uint Attributes)> groups;
        List<string> privileges;

        using (worker.Process)
        {
            Assert.True(await TestTimes.EndOf(worker.Process.OpenAsync(Connection(), TestContext.Current.CancellationToken)));

            using SafeAccessTokenHandle token = TokenOf((await ResultOfAsync(worker, "pid")).GetInt32());
            groups = WorkerToken.Groups(token);
            privileges = WorkerToken.Privileges(token);
        }

        SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        Assert.All(
            groups.Where(group => group.Sid == administrators),
            group => Assert.Equal(DenyOnly, group.Attributes & (DenyOnly | Enabled)));
        Assert.Equal(["SeChangeNotifyPrivilege"], privileges);
    }

    // A connection line of a base on its cluster, which the test program answers with {"ready":true}.
    private static WorkerConnectionLine Connection()
    {
        return new WorkerConnectionLine
        {
            Location = "Srvr=\"srv-1c:1541\";Ref=\"accounting\";",
            User = "outpost-read",
            Password = "read-S3cret",
            SafeMode = true,
            Executor = @"C:\Program Files\Moltaro.OnecOutpost\OutpostExecutor.epf",
        };
    }

    // The result of the script of the test program, which succeeds with it at its true size.
    private static async Task<JsonElement> ResultOfAsync(CheckWorker worker, string script)
    {
        (long Size, byte[] Outcome)? given = await TestTimes.EndOf(
            worker.Process.RunAsync(new WorkerCommandLine(script, "{}", null, 1024), TestContext.Current.CancellationToken));

        Assert.NotNull(given);
        Assert.Equal(given.Value.Outcome.Length, given.Value.Size);

        using JsonDocument document = JsonDocument.Parse(given.Value.Outcome);

        return document.RootElement.GetProperty("result").Clone();
    }

    // The primary token of the process, for its groups and privileges.
    private static SafeAccessTokenHandle TokenOf(int processId)
    {
        using SafeProcessHandle process = OpenProcess(QueryLimitedInformation, inheritHandle: false, (uint)processId);

        if (process.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (!OpenProcessToken(process, TokenQuery, out SafeAccessTokenHandle token))
        {
            int error = Marshal.GetLastPInvokeError();

            token.Dispose();

            throw new Win32Exception(error);
        }

        return token;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
