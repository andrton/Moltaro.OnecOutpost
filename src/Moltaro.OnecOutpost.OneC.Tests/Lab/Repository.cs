namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// Where the tests find what they use. The worker program is built by the reference to its project and copied next to
// the tests. The files of the repository are under src, the folder that holds the solution, found upwards from the
// folder of the tests, and the documents beside it, so that neither the configuration of the build nor the place of
// the clone matters.
internal static class Repository
{
    public static string WorkerPath => Path.Combine(AppContext.BaseDirectory, "Moltaro.OnecOutpost.ComWorker.exe");

    // The executor, built from its sources in src/Executor.
    public static string ExecutorPath => Path.Combine(SourceDirectory, "Executor", "OutpostExecutor.epf");

    public static string LabFilePath => Path.Combine(SourceDirectory, "OneC.TestConfiguration", "lab.local.json");

    // Guide G2, whose scripts a test reads from its Markdown and runs on the test base.
    public static string ScriptGuidePath => Path.GetFullPath(Path.Combine(SourceDirectory, "..", "docs", "guides", "scripts.md"));

    private static string SourceDirectory
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Moltaro.OnecOutpost.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException("No folder above the tests holds Moltaro.OnecOutpost.slnx.");
        }
    }
}
