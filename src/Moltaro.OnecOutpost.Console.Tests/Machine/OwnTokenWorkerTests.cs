using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// A worker of check under the console's own token on Windows, with Requires=Windows alone, since CreateProcessAsUserW
// needs no privilege for the caller's own token: the test program, which stands in for the COM worker, starts with
// the base key and read as its command line, in a job of its own, which holds it and not the console, speaks the
// worker's lines, and what it writes to its standard error is the worker's Diagnostics, read to its end once the
// worker was disposed. Its token is a restricted version of the console's own, so that the code of a base runs
// without the rights of an administrator: Administrators,
// when the console's token holds it, for deny only, no privilege but SeChangeNotifyPrivilege, and an integrity level no
// higher than medium, so that the base's code cannot send input to the elevated programs of the desktop; and the user as
// its default owner, with a default list that grants the user, so that the worker reopens what it creates.
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

    // TokenIntegrityLevel of TOKEN_INFORMATION_CLASS, ERROR_INSUFFICIENT_BUFFER of the first call that asks for its
    // size, and SECURITY_MANDATORY_MEDIUM_RID.
    private const int TokenIntegrityLevelClass = 25;

    private const int InsufficientBuffer = 122;

    private const uint MediumIntegrity = 0x2000;

    // TokenOwner and TokenDefaultDacl of TOKEN_INFORMATION_CLASS, and GENERIC_ALL.
    private const int TokenOwnerClass = 4;

    private const int TokenDefaultDaclClass = 6;

    private const int GenericAll = 0x10000000;

    private static string TestProgram => Path.Combine(AppContext.BaseDirectory, "Moltaro.OnecOutpost.TestWorker.exe");

    [Fact]
    public async Task A_worker_under_the_console_own_token_runs_in_read_mode_in_a_job_of_its_own_and_its_standard_error_is_its_diagnostics()
    {
        ConsoleWorker worker = new WindowsWorkerStarts(TimeProvider.System).Start(null, TestProgram, "accounting");
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

    // CreateRestrictedToken with DISABLE_MAX_PRIVILEGE and Administrators deny-only, then medium integrity: an
    // elevated console holds Administrators, the privileges of
    // an administrator and high integrity, a console that is not elevated holds Administrators for deny only already, or
    // not at all, privileges of its own beside SeChangeNotifyPrivilege, and medium integrity.
    [Fact]
    public async Task A_worker_under_the_console_own_token_holds_Administrators_for_deny_only_no_privilege_but_change_notify_and_at_most_medium_integrity()
    {
        ConsoleWorker worker = new WindowsWorkerStarts(TimeProvider.System).Start(null, TestProgram, "accounting");
        List<(SecurityIdentifier Sid, uint Attributes)> groups;
        List<string> privileges;
        uint integrity;

        using (worker.Process)
        {
            Assert.True(await TestTimes.EndOf(worker.Process.OpenAsync(Connection(), TestContext.Current.CancellationToken)));

            using SafeAccessTokenHandle token = TokenOf((await ResultOfAsync(worker, "pid")).GetInt32());
            groups = WorkerToken.Groups(token);
            privileges = WorkerToken.Privileges(token);
            integrity = IntegrityOf(token);
        }

        SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        Assert.All(
            groups.Where(group => group.Sid == administrators),
            group => Assert.Equal(DenyOnly, group.Attributes & (DenyOnly | Enabled)));
        Assert.Equal(["SeChangeNotifyPrivilege"], privileges);
        Assert.True(integrity <= MediumIntegrity, $"The worker runs at the integrity level 0x{integrity:X4}, above medium.");
    }

    // The default owner and list of the restricted token are the user's, as a token that is not elevated has them: an
    // elevated console's are Administrators and a list for
    // Administrators and SYSTEM, through which a worker that holds Administrators for deny only reopens nothing it creates.
    [Fact]
    public async Task A_worker_under_the_console_own_token_has_the_user_as_its_default_owner_and_a_default_list_that_grants_the_user_all_access()
    {
        ConsoleWorker worker = new WindowsWorkerStarts(TimeProvider.System).Start(null, TestProgram, "accounting");
        SecurityIdentifier owner;
        RawAcl? list;

        using (worker.Process)
        {
            Assert.True(await TestTimes.EndOf(worker.Process.OpenAsync(Connection(), TestContext.Current.CancellationToken)));

            using SafeAccessTokenHandle token = TokenOf((await ResultOfAsync(worker, "pid")).GetInt32());
            owner = OwnerOf(token);
            list = DefaultListOf(token);
        }

        using WindowsIdentity test = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = test.User!;
        Assert.Equal(user, owner);
        Assert.NotNull(list);
        Assert.Contains(
            list.Cast<GenericAce>(),
            ace => ace is CommonAce { AceQualifier: AceQualifier.AccessAllowed } allowed
                && allowed.SecurityIdentifier == user
                && (allowed.AccessMask & GenericAll) == GenericAll);
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
    private static async Task<JsonElement> ResultOfAsync(ConsoleWorker worker, string script)
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

    // The RID of the token's integrity level, the last sub-authority of its label S-1-16-{rid}: TOKEN_MANDATORY_LABEL,
    // whose Label begins with the pointer to that SID.
    private static unsafe uint IntegrityOf(SafeAccessTokenHandle token)
    {
        if (!GetTokenInformation(token, TokenIntegrityLevelClass, null, 0, out int length)
            && Marshal.GetLastPInvokeError() != InsufficientBuffer)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        byte[] label = new byte[length];

        fixed (byte* start = label)
        {
            if (!GetTokenInformation(token, TokenIntegrityLevelClass, start, length, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            string sid = new SecurityIdentifier(*(nint*)start).Value;

            return uint.Parse(sid[(sid.LastIndexOf('-') + 1)..], CultureInfo.InvariantCulture);
        }
    }

    // The token's default owner, TOKEN_OWNER, a pointer to its SID.
    private static SecurityIdentifier OwnerOf(SafeAccessTokenHandle token)
    {
        nint sid = PointerOf(token, TokenOwnerClass, out byte[] buffer);
        SecurityIdentifier owner = new(sid);

        GC.KeepAlive(buffer);

        return owner;
    }

    // The token's default list, TOKEN_DEFAULT_DACL, a pointer to an ACL whose second word is its length; null for none.
    private static unsafe RawAcl? DefaultListOf(SafeAccessTokenHandle token)
    {
        nint acl = PointerOf(token, TokenDefaultDaclClass, out byte[] buffer);

        if (acl == 0)
        {
            return null;
        }

        byte[] binary = new byte[*(ushort*)(acl + 2)];
        Marshal.Copy(acl, binary, 0, binary.Length);
        GC.KeepAlive(buffer);

        return new RawAcl(binary, 0);
    }

    // The pointer with which the token's information of the class begins, such as TOKEN_OWNER's, into the buffer that
    // holds what it points at, pinned, which the caller keeps alive while it reads there.
    private static unsafe nint PointerOf(SafeAccessTokenHandle token, int informationClass, out byte[] buffer)
    {
        if (!GetTokenInformation(token, informationClass, null, 0, out int length)
            && Marshal.GetLastPInvokeError() != InsufficientBuffer)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        buffer = GC.AllocateArray<byte>(length, pinned: true);

        fixed (byte* start = buffer)
        {
            if (!GetTokenInformation(token, informationClass, start, length, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return *(nint*)start;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int informationClass,
        byte* information,
        int length,
        out int returned);
}
