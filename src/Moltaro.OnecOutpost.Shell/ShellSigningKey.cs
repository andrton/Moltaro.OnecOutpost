using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Moltaro.OnecOutpost.Shell;

internal static partial class ShellSigningKey
{
    private const int Exists = 17; // POSIX EEXIST on the supported Unix platforms.

    public static string ReadOrCreate(string folder)
    {
        string directory = Path.GetFullPath(folder);
        string path = Path.Combine(directory, "signing-key.pem");

        try
        {
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            // Only a missing key may be created. The server validates the contents of an existing file.
        }

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pem = key.ExportPkcs8PrivateKeyPem();
        string temporary = Path.Combine(directory, $".signing-key-{Guid.NewGuid():N}.tmp");
        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        FileStream stream = new(temporary, options);

        try
        {
            using (stream)
            {
                stream.Write(Encoding.UTF8.GetBytes(pem));
                stream.Flush(flushToDisk: true);
            }

            return Publish(temporary, path) ? pem : File.ReadAllText(path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // Publish only the complete file. Every shell opening the directory uses the same key if another one won the race.
    private static bool Publish(string temporary, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.Move(temporary, path, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
            }
        }

        // The Unix implementation of File.Move can check for the destination and then rename over it. link is atomic
        // and leaves permissions unchanged because the published name refers to the temporary file's inode.
        if (Link(temporary, path) == 0)
        {
            return true;
        }

        int error = Marshal.GetLastPInvokeError();
        if (error == Exists)
        {
            return false;
        }

        throw new IOException("Could not publish the signing key.", new Win32Exception(error));
    }

    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Link(string existingPath, string newPath);
}
