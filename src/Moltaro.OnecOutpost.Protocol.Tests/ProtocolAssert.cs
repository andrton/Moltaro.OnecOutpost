namespace Moltaro.OnecOutpost.Protocol.Tests;

// A refusal of the codec or of the signing types is stated by its code, never by its text.
internal static class ProtocolAssert
{
    // Asserts that the call refuses what it read with the code, and gives the refusal for what else a test states of
    // it, such as the identity of the command it names.
    public static OutpostProtocolException Refused(string code, Action read)
    {
        OutpostProtocolException refusal = Assert.Throws<OutpostProtocolException>(read);

        Assert.Equal(code, refusal.Code);

        return refusal;
    }
}
