using Moltaro.OnecOutpost.Console.Rights;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The rights of the accounts of a test's machine, as LSA keeps them. Each call is first a line of the operation list of
// the machine's data folder: "look up {account}", "held {sid}", "grant {sid} {right}, {right}" or "revoke {sid} {right}, {right}",
// the rights in the order given. A lookup finds the local users the test names, by name ignoring case, and nothing else;
// a read of the rights held, a grant or a revoke fails with the exception the test gave for it, as LSA fails one with a
// Win32Exception. Not thread-safe.
internal sealed class FakeAccountRights : IAccountRights
{
    private readonly MemoryDataFolder _notes;

    public FakeAccountRights(MemoryDataFolder notes)
    {
        _notes = notes;
    }

    // The local users of the machine, such as {machine}\outpost-worker, with their SIDs.
    public Dictionary<string, string> Users { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The rights each SID holds itself, as LSA enumerates them; none for a SID the test names not.
    public Dictionary<string, IReadOnlyList<string>> Rights { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? HeldFailure { get; set; }

    public Exception? GrantFailure { get; set; }

    public Exception? RevokeFailure { get; set; }

    public string? UserSid(string account)
    {
        _notes.Note($"look up {account}");

        return Users.GetValueOrDefault(account);
    }

    public IReadOnlyList<string> Held(string sid)
    {
        _notes.Note($"held {sid}");

        if (HeldFailure is not null)
        {
            throw HeldFailure;
        }

        return Rights.GetValueOrDefault(sid) ?? [];
    }

    public void Grant(string sid, IReadOnlyList<string> rights)
    {
        _notes.Note($"grant {sid} {string.Join(", ", rights)}");

        if (GrantFailure is not null)
        {
            throw GrantFailure;
        }
    }

    public void Revoke(string sid, IReadOnlyList<string> rights)
    {
        _notes.Note($"revoke {sid} {string.Join(", ", rights)}");

        if (RevokeFailure is not null)
        {
            throw RevokeFailure;
        }
    }
}
