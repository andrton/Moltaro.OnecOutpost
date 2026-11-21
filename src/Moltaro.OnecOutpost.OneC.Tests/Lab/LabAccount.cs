namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// A user of a base and its password. A class, not a record, so that nothing prints the password by accident.
internal sealed class LabAccount
{
    public required string User { get; init; }

    public required string Password { get; init; }
}
