namespace Moltaro.OnecOutpost.Agent.Secrets;

// The protection of the secrets for this machine: the boundary that DPAPI implements and tests replace.
internal interface IMachineProtection
{
    // Encrypts the data for the machine. CryptographicException when it cannot.
    byte[] Protect(byte[] data);

    // Reverses Protect. CryptographicException when it cannot, such as for a file copied from another machine.
    byte[] Unprotect(byte[] data);
}
