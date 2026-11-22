namespace Moltaro.OnecOutpost.Agent.Checks;

// What the checks of §7 decide for one command of a poll answer (concept rule 1).
internal enum CommandVerdictEnum
{
    // Every check passed: the command is received and runs.
    Runs,

    // A check failed: the command is refused with the code of the first check of §7 that failed.
    Refused,

    // The audit log already knows the identity: the command ran, or was refused, before, and nothing is done again.
    Known,
}
