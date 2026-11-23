using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Access;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// A service of a test's own on this machine, MoltaroOnecOutpostTest-{guid:N}, whose virtual account stands for the
// agent's and gets every right the console grants, the batch right too, so that no local user is ever made (the S10
// brief, "Tests with Requires=Windows and Requires=Administrator"); and its own folders: the program folder under
// %ProgramFiles%, whose parents admit nobody else, the data folder under %ProgramData%, and the folder of the unpacked
// archive under the temporary folder. A test creates the service through the console's boundary and reads it back with
// its own calls, QueryServiceConfigW and QueryServiceConfig2W. A test runs through Run or RunAsync, which remove in
// finally the service if it exists, every right of its account through LSA, and the three folders, each part whatever
// the others did. A removal that fails never hides the test's own failure: the test fails with both, its own first.
[SupportedOSPlatform("windows")]
internal sealed partial class TestService
{
    // SERVICE_WIN32_OWN_PROCESS and SERVICE_AUTO_START, as QueryServiceConfigW gives them.
    public const uint OwnProcess = 0x10;

    public const uint AutomaticStart = 2;

    // SC_ACTION_RESTART.
    public const int Restart = 1;

    private const uint ScManagerConnect = 0x1;

    private const uint ServiceQueryConfig = 0x1;

    private const uint ServiceChangeConfig = 0x2;

    private const uint ServiceQueryStatus = 0x4;

    private const uint ServiceStart = 0x10;

    private const uint DeleteAccess = 0x10000;

    private const int ErrorInsufficientBuffer = 122;

    private const int ErrorServiceDoesNotExist = 1060;

    private const int ErrorServiceMarkedForDelete = 1072;

    // The levels of QueryServiceConfig2W and ChangeServiceConfig2W.
    private const uint DescriptionLevel = 1;

    private const uint FailureActionsLevel = 2;

    private const uint FailureActionsFlagLevel = 4;

    private const uint RequiredPrivilegesLevel = 6;

    // POLICY_LOOKUP_NAMES, which LsaRemoveAccountRights needs, and STATUS_OBJECT_NAME_NOT_FOUND, for an account that
    // holds no right.
    private const uint PolicyLookupNames = 0x800;

    private const uint StatusObjectNameNotFound = 0xC0000034;

    public TestService()
    {
        Name = $"MoltaroOnecOutpostTest-{Guid.NewGuid():N}";
        ProgramFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name);
        DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name);
        Archive = Path.Combine(Path.GetTempPath(), Name);
    }

    public static SecurityIdentifier Administrators { get; } = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public string Name { get; }

    public string Account => $@"NT SERVICE\{Name}";

    public string Sid => ServiceAccount.Sid(Name);

    public string ProgramFolder { get; }

    public string DataFolder { get; }

    public string Archive { get; }

    // Runs the test on a service of its own, and removes the service, its rights and its folders in finally.
    public static void Run(Action<TestService> test)
    {
        TestService service = new();
        Exception? failure = null;

        try
        {
            test(service);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            service.Remove(failure);
        }
    }

    public static async Task RunAsync(Func<TestService, Task> test)
    {
        TestService service = new();
        Exception? failure = null;

        try
        {
            await test(service);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            service.Remove(failure);
        }
    }

    // The owner, the control flags and each entry of the access list of a descriptor in SDDL, one per line, the entries
    // in ordinal order, so that two descriptors compare alike whatever the order of their entries and nothing else: the
    // order of allow entries changes no access, and .NET reads explicit entries sorted by their SIDs, SYSTEM before
    // Administrators, whatever the order they were written in.
    public static string Unordered(string sddl)
    {
        RawSecurityDescriptor descriptor = new(sddl);
        IEnumerable<string> entries = descriptor.DiscretionaryAcl!
            .Cast<GenericAce>()
            .Select(entry => entry is KnownAce known
                ? $"{known.AceType} {known.AceFlags} 0x{known.AccessMask:x} {known.SecurityIdentifier}"
                : $"{entry.AceType} {entry.AceFlags}")
            .Order(StringComparer.Ordinal);

        return string.Join('\n', [$"owner {descriptor.Owner}", $"control {descriptor.ControlFlags}", .. entries]);
    }

    // The owner and the access list of a folder or a file, as Unordered gives them.
    public static string ListsOf(string path)
    {
        const AccessControlSections OwnerAndAccess = AccessControlSections.Owner | AccessControlSections.Access;

        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl(OwnerAndAccess)
            : new FileInfo(path).GetAccessControl(OwnerAndAccess);

        return Unordered(security.GetSecurityDescriptorSddlForm(OwnerAndAccess));
    }

    // Whether the service controller knows the service and has not marked it for deletion.
    public bool Exists()
    {
        nint service = TryOpen(ServiceQueryStatus);

        if (service == 0)
        {
            return false;
        }

        Close(service);

        return true;
    }

    // A handle of the service that the test holds, and closes with Close.
    public nint Open()
    {
        nint service = TryOpen(ServiceQueryStatus);

        return service != 0 ? service : throw new Win32Exception(ErrorServiceDoesNotExist);
    }

    public static void Close(nint handle)
    {
        if (handle != 0)
        {
            CloseServiceHandle(handle);
        }
    }

    // What QueryServiceConfigW gives.
    public (uint Type, uint Start, string BinaryPath, string Account, string DisplayName) Configuration()
    {
        return WithService(ServiceQueryConfig, service =>
        {
            QueryServiceConfig(service, 0, 0, out uint needed);
            ThrowUnless(Marshal.GetLastPInvokeError() == ErrorInsufficientBuffer);

            nint buffer = Marshal.AllocHGlobal((int)needed);

            try
            {
                ThrowUnless(QueryServiceConfig(service, buffer, needed, out _));
                QueryConfiguration configuration = Marshal.PtrToStructure<QueryConfiguration>(buffer);

                return (
                    configuration.ServiceType,
                    configuration.StartType,
                    Marshal.PtrToStringUni(configuration.BinaryPathName) ?? "",
                    Marshal.PtrToStringUni(configuration.ServiceStartName) ?? "",
                    Marshal.PtrToStringUni(configuration.DisplayName) ?? "");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        });
    }

    // The description of the service, null for none.
    public string? Description()
    {
        return Query(DescriptionLevel, buffer => Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer)));
    }

    // The period after which the count of failures is reset, in seconds, and the actions in their order, each its type
    // and its delay in milliseconds.
    public (uint ResetPeriod, (int Type, uint Delay)[] Actions) FailureActions()
    {
        return Query(FailureActionsLevel, buffer =>
        {
            FailureActionList list = Marshal.PtrToStructure<FailureActionList>(buffer);
            (int Type, uint Delay)[] actions = new (int Type, uint Delay)[list.ActionCount];

            for (int index = 0; index < actions.Length; index++)
            {
                FailureAction action = Marshal.PtrToStructure<FailureAction>(list.Actions + (index * Marshal.SizeOf<FailureAction>()));
                actions[index] = (action.Type, action.Delay);
            }

            return (list.ResetPeriod, actions);
        });
    }

    // Whether the failure actions run also when the service stops with an error of its own.
    public bool FailureActionsOnNonCrashFailures()
    {
        return Query(FailureActionsFlagLevel, buffer => Marshal.ReadInt32(buffer) != 0);
    }

    // The privileges that the service requires, which would be the only ones of its token.
    public IReadOnlyList<string> RequiredPrivileges()
    {
        return Query(RequiredPrivilegesLevel, buffer =>
        {
            List<string> privileges = [];
            nint next = Marshal.ReadIntPtr(buffer);

            while (next != 0 && Marshal.PtrToStringUni(next) is { Length: > 0 } privilege)
            {
                privileges.Add(privilege);
                next += (privilege.Length + 1) * sizeof(char);
            }

            return privileges;
        });
    }

    // Sets the failure actions by hand, as an administrator may in the Services console.
    public void SetFailureActions(uint resetPeriod, params (int Type, uint Delay)[] actions)
    {
        // A restart action needs SERVICE_START on the handle.
        WithService(ServiceChangeConfig | ServiceStart, service =>
        {
            int size = Marshal.SizeOf<FailureAction>();
            nint list = Marshal.AllocHGlobal(size * actions.Length);

            try
            {
                for (int index = 0; index < actions.Length; index++)
                {
                    Marshal.StructureToPtr(new FailureAction { Type = actions[index].Type, Delay = actions[index].Delay }, list + (index * size), false);
                }

                FailureActionList info = new() { ResetPeriod = resetPeriod, ActionCount = (uint)actions.Length, Actions = list };
                nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<FailureActionList>());

                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    ThrowUnless(ChangeServiceConfig2(service, FailureActionsLevel, buffer));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(list);
            }
        });
    }

    // Deletes the service if it exists, takes every right of its account, and removes the folders, each whatever the
    // others did. Then the test's failure passes as it was, or, when a part failed too, with the failures of the parts
    // beside it; without a failure of the test, a part that failed fails it.
    private void Remove(Exception? failure)
    {
        List<Exception> failures = [];

        Try(failures, DeleteIfExists);
        Try(failures, RemoveRights);
        Try(failures, () => RemoveFolder(ProgramFolder));
        Try(failures, () => RemoveFolder(DataFolder));
        Try(failures, () => RemoveFolder(Archive));

        if (failures.Count == 0)
        {
            if (failure is not null)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            return;
        }

        if (failure is null)
        {
            throw new AggregateException("The test's service, rights or folders were not all removed.", failures);
        }

        throw new AggregateException(
            "The test failed, and its service, rights or folders were not all removed.",
            [failure, .. failures]);
    }

    private static void Try(List<Exception> failures, Action part)
    {
        try
        {
            part();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void DeleteIfExists()
    {
        nint service = TryOpen(DeleteAccess);

        if (service == 0)
        {
            return;
        }

        try
        {
            ThrowUnless(DeleteService(service) || Marshal.GetLastPInvokeError() == ErrorServiceMarkedForDelete);
        }
        finally
        {
            Close(service);
        }
    }

    private void RemoveRights()
    {
        SecurityIdentifier account = new(Sid);
        byte[] sid = new byte[account.BinaryLength];
        account.GetBinaryForm(sid, 0);

        LsaAttributes attributes = new() { Length = (uint)Marshal.SizeOf<LsaAttributes>() };
        ThrowUnlessLsa(LsaOpenPolicy(0, in attributes, PolicyLookupNames, out nint policy));

        try
        {
            uint status = LsaRemoveAccountRights(policy, sid, allRights: true, 0, 0);

            if (status != StatusObjectNameNotFound)
            {
                ThrowUnlessLsa(status);
            }
        }
        finally
        {
            LsaClose(policy);
        }
    }

    private static void RemoveFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(folder, recursive: true);
    }

    private T Query<T>(uint level, Func<nint, T> read)
    {
        return WithService(ServiceQueryConfig, service =>
        {
            QueryServiceConfig2(service, level, 0, 0, out uint needed);
            ThrowUnless(Marshal.GetLastPInvokeError() == ErrorInsufficientBuffer);

            nint buffer = Marshal.AllocHGlobal((int)needed);

            try
            {
                ThrowUnless(QueryServiceConfig2(service, level, buffer, needed, out _));

                return read(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        });
    }

    private T WithService<T>(uint access, Func<nint, T> use)
    {
        nint service = TryOpen(access);

        if (service == 0)
        {
            throw new Win32Exception(ErrorServiceDoesNotExist);
        }

        try
        {
            return use(service);
        }
        finally
        {
            Close(service);
        }
    }

    private void WithService(uint access, Action<nint> use)
    {
        WithService(access, service =>
        {
            use(service);

            return 0;
        });
    }

    // A handle of the service with the access given, or 0 when the service controller knows no such service or has marked
    // it for deletion.
    private nint TryOpen(uint access)
    {
        nint manager = OpenSCManager(null, null, ScManagerConnect);
        ThrowUnless(manager != 0);

        try
        {
            nint service = OpenService(manager, Name, access);

            int error = Marshal.GetLastPInvokeError();

            if (service == 0 && error != ErrorServiceDoesNotExist && error != ErrorServiceMarkedForDelete)
            {
                throw new Win32Exception(error);
            }

            return service;
        }
        finally
        {
            Close(manager);
        }
    }

    private static void ThrowUnless(bool done)
    {
        if (!done)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static void ThrowUnlessLsa(uint status)
    {
        if (status != 0)
        {
            throw new Win32Exception(LsaNtStatusToWinError(status));
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenService(nint manager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceConfig(nint service, nint configuration, uint size, out uint needed);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceConfig2(nint service, uint level, nint buffer, uint size, out uint needed);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig2(nint service, uint level, nint info);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(nint service);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaOpenPolicy(nint systemName, in LsaAttributes attributes, uint desiredAccess, out nint policy);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaRemoveAccountRights(
        nint policy,
        byte[] sid,
        [MarshalAs(UnmanagedType.U1)] bool allRights,
        nint rights,
        uint count);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaClose(nint handle);

    [LibraryImport("advapi32.dll")]
    private static partial int LsaNtStatusToWinError(uint status);
}
