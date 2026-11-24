using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Moltaro.OnecOutpost.Console.Rights;

// The rights of accounts through LSA: LsaOpenPolicy with POLICY_LOOKUP_NAMES | POLICY_CREATE_ACCOUNT, no more;
// LsaAddAccountRights, which ignores a right held; LsaRemoveAccountRights without AllRights, an account unknown to LSA
// no error; NTSTATUS through LsaNtStatusToWinError. A right reaches a token at the account's next logon. Every handle
// and every string given to LSA is freed by its owner, once.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsAccountRights : IAccountRights
{
    // POLICY_LOOKUP_NAMES, which the calls on the rights of an account need, and POLICY_CREATE_ACCOUNT, which
    // LsaAddAccountRights needs for an account that LSA does not know yet.
    private const uint PolicyAccess = 0x00000800 | 0x00000010;

    // STATUS_OBJECT_NAME_NOT_FOUND: LSA knows no account of the SID, so it holds no right.
    private const uint ObjectNameNotFound = 0xC0000034;

    // ERROR_INSUFFICIENT_BUFFER and ERROR_NONE_MAPPED of LookupAccountNameW, and SidTypeUser of SID_NAME_USE.
    private const int InsufficientBuffer = 122;

    private const int NoneMapped = 1332;

    private const int UserType = 1;

    public string? UserSid(string account)
    {
        uint sidSize = 0;
        uint domainSize = 0;

        // The first call gives the sizes of the SID and of the name of its domain, or says that Windows names no account.
        if (!LookupAccountNameW(null, account, null, ref sidSize, null, ref domainSize, out _))
        {
            int error = Marshal.GetLastPInvokeError();

            if (error == NoneMapped)
            {
                return null;
            }

            if (error != InsufficientBuffer)
            {
                throw new Win32Exception(error);
            }
        }

        byte[] sid = new byte[sidSize];
        char[] domain = new char[domainSize];

        if (!LookupAccountNameW(null, account, sid, ref sidSize, domain, ref domainSize, out int use))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return use == UserType ? new SecurityIdentifier(sid, 0).Value : null;
    }

    public void Grant(string sid, IReadOnlyList<string> rights)
    {
        using PolicyHandle policy = OpenPolicy();

        WithRights(rights, names => Check(LsaAddAccountRights(policy, BinaryOf(sid), names, (uint)names.Length)));
    }

    public void Revoke(string sid, IReadOnlyList<string> rights)
    {
        using PolicyHandle policy = OpenPolicy();

        WithRights(rights, names =>
        {
            uint status = LsaRemoveAccountRights(policy, BinaryOf(sid), allRights: false, names, (uint)names.Length);

            if (status != ObjectNameNotFound)
            {
                Check(status);
            }
        });
    }

    public IReadOnlyList<string> Held(string sid)
    {
        using PolicyHandle policy = OpenPolicy();

        uint status = LsaEnumerateAccountRights(policy, BinaryOf(sid), out nint rights, out uint count);

        if (status == ObjectNameNotFound)
        {
            return [];
        }

        Check(status);

        try
        {
            List<string> held = [];
            int size = Marshal.SizeOf<UnicodeString>();

            for (int index = 0; index < count; index++)
            {
                UnicodeString right = Marshal.PtrToStructure<UnicodeString>(rights + (index * size));

                held.Add(Marshal.PtrToStringUni(right.Buffer, right.Length / sizeof(char)));
            }

            return held;
        }
        finally
        {
            LsaFreeMemory(rights);
        }
    }

    private static PolicyHandle OpenPolicy()
    {
        ObjectAttributes attributes = new() { Length = (uint)Marshal.SizeOf<ObjectAttributes>() };
        uint status = LsaOpenPolicy(0, in attributes, PolicyAccess, out PolicyHandle policy);

        if (status != 0)
        {
            policy.Dispose();
            Check(status);
        }

        return policy;
    }

    // The rights as the strings of LSA, each in memory of its own for the call, then freed.
    private static void WithRights(IReadOnlyList<string> rights, Action<UnicodeString[]> call)
    {
        UnicodeString[] names = new UnicodeString[rights.Count];

        try
        {
            for (int index = 0; index < names.Length; index++)
            {
                ushort length = (ushort)(rights[index].Length * sizeof(char));

                names[index] = new UnicodeString
                {
                    Length = length,
                    MaximumLength = (ushort)(length + sizeof(char)),
                    Buffer = Marshal.StringToHGlobalUni(rights[index]),
                };
            }

            call(names);
        }
        finally
        {
            // FreeHGlobal of zero, a name not yet made, does nothing.
            foreach (UnicodeString name in names)
            {
                Marshal.FreeHGlobal(name.Buffer);
            }
        }
    }

    private static byte[] BinaryOf(string sid)
    {
        SecurityIdentifier account = new(sid);
        byte[] binary = new byte[account.BinaryLength];

        account.GetBinaryForm(binary, 0);

        return binary;
    }

    private static void Check(uint status)
    {
        if (status != 0)
        {
            throw new Win32Exception(LsaNtStatusToWinError(status));
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupAccountNameW(
        string? systemName,
        string accountName,
        [Out] byte[]? sid,
        ref uint sidSize,
        [Out] char[]? referencedDomainName,
        ref uint referencedDomainNameSize,
        out int use);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaOpenPolicy(nint systemName, in ObjectAttributes attributes, uint desiredAccess, out PolicyHandle policy);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaAddAccountRights(PolicyHandle policy, byte[] sid, UnicodeString[] rights, uint count);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaRemoveAccountRights(
        PolicyHandle policy,
        byte[] sid,
        [MarshalAs(UnmanagedType.U1)] bool allRights,
        UnicodeString[] rights,
        uint count);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaEnumerateAccountRights(PolicyHandle policy, byte[] sid, out nint rights, out uint count);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaFreeMemory(nint buffer);

    [LibraryImport("advapi32.dll")]
    private static partial int LsaNtStatusToWinError(uint status);
}
