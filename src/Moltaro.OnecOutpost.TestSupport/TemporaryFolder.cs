namespace Moltaro.OnecOutpost.TestSupport;

// A folder of a test's own under the temporary directory, for a test that needs the disk. Disposing it removes it with
// everything in it, read-only files included.
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = Directory.CreateTempSubdirectory("outpost-tests-").FullName;
    }

    public string Path { get; }

    public string PathOf(string relativePath)
    {
        return System.IO.Path.Combine(Path, relativePath);
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Path, recursive: true);
    }
}
