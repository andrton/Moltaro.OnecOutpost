using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Console.Folders;

// The folders as NTFS keeps them. It makes Administrators the owner of every entry it writes, as an elevated token may.
// The data folder: O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;FR;;;{agent}), which CreateDirectoryW fails to make
// with 183 when anything is there, so that only a folder judged by the agent's Others is ever applied to; audit\ and
// outcomes\ inherit it plus (A;OICI;0x1301bf;;;{agent}), made without a descriptor so that they inherit it. The program
// folder: O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU). A descriptor is written by
// SetNamedSecurityInfo, which passes the entries to inherit on to what the folder holds, so the files of the data folder
// get them without being written; .NET writes the explicit entries in the order of their SIDs, SYSTEM first, which
// changes no access. A failure of Windows in its own calls is an IOException with the Win32Exception inside; those of
// .NET pass as .NET throws them. Kind counts a file that is not a folder as Absent, so that nothing takes it for a
// folder, and CreateDirectoryW then fails with 183.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsFolderSetup : IFolderSetup
{
    private const string ProgramFolderDescriptor = "O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU)";

    // An entry of the program folder: Administrators as its owner and only the entries it inherits.
    private const string InheritedOnlyDescriptor = "O:BAD:AI";

    // What the console writes of a descriptor: the owner and the access list, never the group or the audit list, whose
    // writing needs a privilege of its own.
    private const AccessControlSections OwnerAndAccess = AccessControlSections.Owner | AccessControlSections.Access;

    // The access 0 and every sharing: the handle only names the entry. FILE_FLAG_BACKUP_SEMANTICS opens a folder, and
    // OPEN_EXISTING makes nothing.
    private const uint NoAccess = 0;

    private const uint ShareAll = 0x1 | 0x2 | 0x4;

    private const uint OpenExisting = 3;

    private const uint BackupSemantics = 0x02000000;

    // ERROR_FILE_NOT_FOUND and ERROR_PATH_NOT_FOUND.
    private const int FileNotFound = 2;

    private const int PathNotFound = 3;

    // The prefixes of the paths that GetFinalPathNameByHandleW gives, of a drive and of a share.
    private const string LongPrefix = @"\\?\";

    private const string LongSharePrefix = @"\\?\UNC\";

    private static readonly string[] WritableFolders = ["audit", "outcomes"];

    public FolderKindEnum Kind(string path)
    {
        FileAttributes attributes;

        try
        {
            // The attributes of the entry itself: a reparse point is not followed.
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return FolderKindEnum.Absent;
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return FolderKindEnum.ReparsePoint;
        }

        return attributes.HasFlag(FileAttributes.Directory) ? FolderKindEnum.Folder : FolderKindEnum.Absent;
    }

    public string FinalPath(string folder)
    {
        using SafeFileHandle handle = CreateFileW(folder, NoAccess, ShareAll, 0, OpenExisting, BackupSemantics, 0);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();

            if (error is FileNotFound or PathNotFound)
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            }

            throw Failed(folder, error);
        }

        // The first call gives the length with the terminating null, the second the length without it.
        uint needed = GetFinalPathNameByHandleW(handle, null, 0, 0);
        char[] buffer = new char[needed];
        uint length = needed == 0 ? 0 : GetFinalPathNameByHandleW(handle, buffer, needed, 0);

        if (length == 0 || length >= needed)
        {
            throw Failed(folder, Marshal.GetLastPInvokeError());
        }

        string path = new(buffer, 0, (int)length);

        if (path.StartsWith(LongSharePrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = @"\\" + path[LongSharePrefix.Length..];
        }
        else if (path.StartsWith(LongPrefix, StringComparison.Ordinal))
        {
            path = path[LongPrefix.Length..];
        }

        return Path.TrimEndingDirectorySeparator(path);
    }

    // The folder first, so that what it then holds inherits its lists; every entry after its folder, so that the lists
    // of a folder inside are set before those of its own entries.
    public void ApplyProgramFolder(string folder, string? source)
    {
        DirectoryInfo programFolder = new(folder);
        DirectorySecurity security = DirectorySecurityOf(ProgramFolderDescriptor);

        // Made with its descriptor, so that the lists of %ProgramFiles% never apply to it, then given it as Windows writes
        // a descriptor, as one that exists is.
        if (!programFolder.Exists)
        {
            programFolder.Create(security);
        }

        programFolder.SetAccessControl(security);

        if (source is not null)
        {
            Empty(programFolder);
            CopyContent(new DirectoryInfo(source), programFolder);
        }

        foreach (FileSystemInfo entry in programFolder.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if (entry is DirectoryInfo inner)
            {
                inner.SetAccessControl(DirectorySecurityOf(InheritedOnlyDescriptor));
            }
            else
            {
                ((FileInfo)entry).SetAccessControl(FileSecurityOf(InheritedOnlyDescriptor));
            }
        }
    }

    public void ApplyDataFolder(string folder, string agentSid, bool create)
    {
        DirectorySecurity security = DirectorySecurityOf($"O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;FR;;;{agentSid})");

        // Made with its descriptor in one call, then given it as Windows writes a descriptor, as one that exists is.
        if (create)
        {
            CreateFolder(folder, security);
        }

        new DirectoryInfo(folder).SetAccessControl(security);

        // The agent deletes expired audit files and confirmed outcomes, so it may modify these two. One that is absent is
        // made without a descriptor, so that it inherits the data folder's lists: Windows would protect a descriptor
        // given at its creation, and the folder would then inherit nothing, Administrators' full control included,
        // without which they may not write its owner. Each gets an object of its own, since a write clears what the
        // object marks as changed.
        foreach (string name in WritableFolders)
        {
            DirectoryInfo inner = new(Path.Combine(folder, name));

            inner.Create();
            inner.SetAccessControl(DirectorySecurityOf($"O:BAD:AI(A;OICI;0x1301bf;;;{agentSid})"));
        }
    }

    public void OwnByAdministrators(string file)
    {
        FileSecurity security = new();

        security.SetSecurityDescriptorSddlForm("O:BA", AccessControlSections.Owner);
        new FileInfo(file).SetAccessControl(security);
    }

    private static DirectorySecurity DirectorySecurityOf(string descriptor)
    {
        DirectorySecurity security = new();

        security.SetSecurityDescriptorSddlForm(descriptor, OwnerAndAccess);

        return security;
    }

    private static FileSecurity FileSecurityOf(string descriptor)
    {
        FileSecurity security = new();

        security.SetSecurityDescriptorSddlForm(descriptor, OwnerAndAccess);

        return security;
    }

    // An update leaves no file of an older version behind.
    private static void Empty(DirectoryInfo folder)
    {
        foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo inner)
            {
                inner.Delete(recursive: true);
            }
            else
            {
                entry.Delete();
            }
        }
    }

    // The folders first, then the files, each by its path in the source.
    private static void CopyContent(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (DirectoryInfo inner in source.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target.FullName, Path.GetRelativePath(source.FullName, inner.FullName)));
        }

        foreach (FileInfo file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.CopyTo(Path.Combine(target.FullName, Path.GetRelativePath(source.FullName, file.FullName)));
        }
    }

    // CreateDirectoryW with the descriptor, which fails where anything is, as FileSystemAclExtensions.Create does not.
    private static unsafe void CreateFolder(string folder, DirectorySecurity security)
    {
        byte[] descriptor = security.GetSecurityDescriptorBinaryForm();

        fixed (byte* bytes = descriptor)
        {
            SecurityAttributes attributes = new()
            {
                Length = (uint)sizeof(SecurityAttributes),
                SecurityDescriptor = (nint)bytes,
            };

            if (!CreateDirectoryW(folder, in attributes))
            {
                throw Failed(folder, Marshal.GetLastPInvokeError());
            }
        }
    }

    private static IOException Failed(string path, int error)
    {
        Win32Exception windows = new(error);

        return new IOException($"{path}: {windows.Message}", windows);
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[]? path, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryW(string pathName, in SecurityAttributes securityAttributes);
}
