using System.Security.Cryptography;
using System.Text;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Checks;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Checks;

// The checks of the agent of PairedFolder: its policy, the server key it pinned at pairing, and the fake clock, which
// stands at TestTimes.Start; over an audit log that holds one Write of an earlier run as received. It makes the
// commands of the tests and signs their exact bytes (§3 of docs/protocol.md) with the server's key, or with another.
internal sealed class TestChecks : IDisposable
{
    // The command of an earlier run, received and not yet ended.
    public static readonly Guid KnownId = Guid.Parse("0b6f2c1d-3e4a-4b5c-8d6e-7f8091a2b3c4");

    public static readonly DateTimeOffset Now = TestTimes.Start;

    // The skew that the commands of the tests carry, five seconds rather than the server's default of two minutes, so
    // that a check shows it judges by the command's skew (rule 1).
    public static readonly TimeSpan Skew = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan TakeTimeout = TimeSpan.FromSeconds(30);

    private readonly ECDsa _serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    // The agent holds only the public key it pinned.
    private readonly ECDsa _pinnedKey;

    public TestChecks()
    {
        DateTimeOffset earlier = Now.AddHours(-1);
        Data.Audit.Seed(FileOf(earlier), Received(earlier, KnownId, OutpostModeEnum.Write));

        FakeClock clock = new(Now);
        _pinnedKey = OutpostKeys.ImportPublicKey(_serverKey.ExportSubjectPublicKeyInfo());
        Log = AuditLog.Open(Data.Audit, clock);
        Checks = new CommandChecks(PairedFolder.Agent, _pinnedKey, AgentPolicyReader.Read(PairedFolder.Policy), clock);
    }

    public MemoryDataFolder Data { get; } = new();

    public AuditLog Log { get; }

    public CommandChecks Checks { get; }

    // A key that is not the server's.
    public ECDsa OtherKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    // A command of a new identity for the agent, of a base and a mode, issued now with the take timeout and the skew of
    // the tests; a Write with a caller key.
    public static OutpostCommandMessage Command(string baseKey, OutpostModeEnum mode)
    {
        OutpostCommandMessage command = TestCommands.Of(
            Guid.NewGuid(),
            mode,
            Now,
            "Result = Parameters[\"date\"];",
            """{"date":"2026-11-20"}""");

        return command with { Base = baseKey, Deadline = Now + TakeTimeout, Skew = Skew };
    }

    // The command issued at another time, with its take timeout.
    public static OutpostCommandMessage IssuedAt(OutpostCommandMessage command, DateTimeOffset issued)
    {
        return command with { Issued = issued, Deadline = issued + TakeTimeout };
    }

    // The command with another take deadline, issued its take timeout before it.
    public static OutpostCommandMessage DeadlineAt(OutpostCommandMessage command, DateTimeOffset deadline)
    {
        return command with { Issued = deadline - TakeTimeout, Deadline = deadline };
    }

    // The identity of the command a check names: the identity of a refusal or of a known command, or that of the command
    // read, for one that runs.
    public static Guid? IdOf(CommandCheck check)
    {
        return check.Command?.Id ?? check.Id;
    }

    // The command as the server's writer writes it, signed with the server's key.
    public OutpostSignedCommand Signed(OutpostCommandMessage command)
    {
        return Signed(_serverKey, OutpostCodec.Write(command));
    }

    // Bytes written by hand, which no server writes, signed with the server's key.
    public OutpostSignedCommand Signed(string text)
    {
        return Signed(_serverKey, Encoding.UTF8.GetBytes(text));
    }

    // The command signed with a key that is not the server's.
    public OutpostSignedCommand Forged(OutpostCommandMessage command)
    {
        return Signed(OtherKey, OutpostCodec.Write(command));
    }

    public void Dispose()
    {
        Log.Dispose();
        _pinnedKey.Dispose();
        _serverKey.Dispose();
        OtherKey.Dispose();
    }

    private static OutpostSignedCommand Signed(ECDsa key, byte[] bytes)
    {
        return new OutpostSignedCommand(bytes, OutpostSignatures.SignCommand(key, bytes));
    }
}
