using System.Runtime.Versioning;
using System.Text;
using Moltaro.OnecOutpost.Agent.Files;

namespace Moltaro.OnecOutpost.Agent.Tests.Files;

// A folder of the data folder on the disk, by file name and without subfolders (the S6a brief, IAgentFolder and
// DiskFolder). Every failure is an IOException, UnauthorizedAccessException included, and nothing retries. Each test
// works in a temporary folder of its own; no test can show a loss of power, so that a flush or a rename reached the
// disk is not tested.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class DiskFolderTests : IDisposable
{
    private readonly TemporaryFolder _temporary = new();

    public void Dispose()
    {
        _temporary.Dispose();
    }

    [Fact]
    public void Names_lists_the_files_of_the_folder_and_no_subfolder()
    {
        File.WriteAllText(_temporary.PathOf("2026-11-22.jsonl"), "");
        File.WriteAllText(_temporary.PathOf("lock"), "");
        Directory.CreateDirectory(_temporary.PathOf("aside"));

        DiskFolder folder = new(_temporary.Path);

        Assert.Equal(["2026-11-22.jsonl", "lock"], folder.Names().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Append_creates_a_file_that_is_absent_and_adds_the_bytes_at_its_end()
    {
        DiskFolder folder = new(_temporary.Path);

        folder.Append("2026-11-22.jsonl", "first\n"u8);
        folder.Append("2026-11-22.jsonl", "second\n"u8);

        Assert.Equal("first\nsecond\n", File.ReadAllText(_temporary.PathOf("2026-11-22.jsonl")));
    }

    [Fact]
    public void OpenRead_reads_a_file_from_its_start_while_another_program_holds_it_open_for_writing()
    {
        string path = _temporary.PathOf("2026-11-22.jsonl");
        File.WriteAllText(path, "first\nsecond\n");
        DiskFolder folder = new(_temporary.Path);

        using FileStream writer = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        writer.Seek(0, SeekOrigin.End);

        using Stream stream = folder.OpenRead("2026-11-22.jsonl");
        using StreamReader reader = new(stream, Encoding.UTF8);

        Assert.Equal("first\nsecond\n", reader.ReadToEnd());
    }

    [Fact]
    public void Cut_sets_the_length_of_a_file()
    {
        string path = _temporary.PathOf("2026-11-22.jsonl");
        File.WriteAllText(path, "first\nsecond");
        DiskFolder folder = new(_temporary.Path);

        folder.Cut("2026-11-22.jsonl", 6);

        Assert.Equal("first\n", File.ReadAllText(path));
    }

    // The temporary file is renamed over the file, so none is left.
    [Fact]
    public void Replace_puts_the_bytes_in_place_of_the_file_and_leaves_no_temporary_file()
    {
        string path = _temporary.PathOf("outcome.json");
        File.WriteAllText(path, "old outcome");
        DiskFolder folder = new(_temporary.Path);

        folder.Replace("outcome.json", "new"u8);
        folder.Replace("absent.json", "created"u8);

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal("created", File.ReadAllText(_temporary.PathOf("absent.json")));
        Assert.Equal(["absent.json", "outcome.json"], folder.Names().Order(StringComparer.Ordinal));
    }

    // A stop may leave {name}.tmp behind; the next Replace of the name writes it anew.
    [Fact]
    public void Replace_writes_over_a_temporary_file_that_a_stop_left()
    {
        File.WriteAllText(_temporary.PathOf("outcome.json.tmp"), "torn outcome that is longer");
        DiskFolder folder = new(_temporary.Path);

        folder.Replace("outcome.json", "whole"u8);

        Assert.Equal("whole", File.ReadAllText(_temporary.PathOf("outcome.json")));
        Assert.Equal(["outcome.json"], folder.Names());
    }

    [Fact]
    public void Delete_removes_a_file_and_accepts_one_that_is_absent()
    {
        File.WriteAllText(_temporary.PathOf("outcome.json"), "outcome");
        DiskFolder folder = new(_temporary.Path);

        folder.Delete("outcome.json");
        folder.Delete("absent.json");

        Assert.Empty(folder.Names());
    }

    // A second copy of the agent finds the lock held (§13 of docs/protocol.md).
    [Fact]
    public void Lock_creates_the_file_and_holds_it_without_sharing_until_it_is_disposed()
    {
        DiskFolder folder = new(_temporary.Path);

        IDisposable held = folder.Lock("lock");

        Assert.True(File.Exists(_temporary.PathOf("lock")));
        Assert.ThrowsAny<IOException>(() => folder.Lock("lock"));
        Assert.ThrowsAny<IOException>(() => new FileStream(_temporary.PathOf("lock"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite).Dispose());

        held.Dispose();

        folder.Lock("lock").Dispose();
    }

    // A missing audit\ records nothing: the lock that a start takes first fails.
    [Fact]
    public void Each_operation_in_a_folder_that_does_not_exist_fails_with_IOException()
    {
        DiskFolder folder = new(_temporary.PathOf("absent"));

        Assert.ThrowsAny<IOException>(() => folder.Lock("lock"));
        Assert.ThrowsAny<IOException>(() => folder.Names());
        Assert.ThrowsAny<IOException>(() => folder.Append("2026-11-22.jsonl", "entry\n"u8));
    }

    [Fact]
    public void OpenRead_and_Cut_of_a_file_that_is_absent_fail_with_IOException()
    {
        DiskFolder folder = new(_temporary.Path);

        Assert.ThrowsAny<IOException>(() => folder.OpenRead("absent.json").Dispose());
        Assert.ThrowsAny<IOException>(() => folder.Cut("absent.json", 0));
    }

    // Windows reports a file that may not be changed with UnauthorizedAccessException, or with a Win32 error from the
    // rename, which the folder turns into an IOException.
    [Theory]
    [InlineData("append")]
    [InlineData("cut")]
    [InlineData("replace")]
    [InlineData("delete")]
    public void A_change_that_Windows_does_not_allow_fails_with_IOException(string operation)
    {
        string path = _temporary.PathOf("read-only.json");
        File.WriteAllText(path, "outcome");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        DiskFolder folder = new(_temporary.Path);

        Action change = operation switch
        {
            "append" => () => folder.Append("read-only.json", "more"u8),
            "cut" => () => folder.Cut("read-only.json", 0),
            "replace" => () => folder.Replace("read-only.json", "new"u8),
            _ => () => folder.Delete("read-only.json"),
        };

        Assert.ThrowsAny<IOException>(change);
        Assert.Equal("outcome", File.ReadAllText(path));
    }

    // The limitation of the brief: a program that holds an audit file open without sharing writing fails the next
    // write of the agent, and the agent stops.
    [Fact]
    public void A_write_to_a_file_that_another_program_holds_without_sharing_writing_fails_with_IOException()
    {
        string path = _temporary.PathOf("2026-11-22.jsonl");
        File.WriteAllText(path, "first\n");
        DiskFolder folder = new(_temporary.Path);

        using FileStream reader = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsAny<IOException>(() => folder.Append("2026-11-22.jsonl", "second\n"u8));
    }
}
