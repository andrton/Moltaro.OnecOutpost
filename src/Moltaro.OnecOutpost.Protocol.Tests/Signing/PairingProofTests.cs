using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Signing;

// The pairing proof of §3 of docs/protocol.md: an HMAC-SHA256, keyed by the 16 bytes of the secret of the token, over
// `outpost/1 pair-proof {agent} {keyHash}`. It binds the agent's key to the secret without sending the secret.
public sealed class PairingProofTests
{
    // HMAC-SHA256, computed outside the codec, with the key 00 01 … 0f, over the ASCII bytes of
    // "outpost/1 pair-proof 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91",
    // whose last field is the SHA-256 of the 91 bytes of the key of Sample.
    private const string ProofHex = "557c6749f591a926b594047a74febd598d92ceb9c5723b85ee83bb9ec71cfa1f";

    private static readonly byte[] Secret = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    private static readonly byte[] Proof = Convert.FromHexString(ProofHex);

    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_proof_is_the_HMAC_of_the_secret_over_the_identity_and_the_hash_of_the_key()
    {
        byte[] proof = OutpostPairingProof.Compute(Secret, Sample.Agent, Sample.PublicKey);

        Assert.Equal(ProofHex, Convert.ToHexStringLower(proof));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_proof_of_the_secret_the_identity_and_the_key_is_accepted()
    {
        Assert.True(OutpostPairingProof.Check(Secret, Sample.Agent, Sample.PublicKey, Proof));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_proof_made_with_another_secret_is_refused()
    {
        byte[] otherSecret = [.. Secret];
        otherSecret[15] ^= 1;

        byte[] proof = OutpostPairingProof.Compute(otherSecret, Sample.Agent, Sample.PublicKey);

        Assert.False(OutpostPairingProof.Check(Secret, Sample.Agent, Sample.PublicKey, proof));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_key_swapped_after_the_proof_was_made_is_refused()
    {
        using System.Security.Cryptography.ECDsa other = Sample.NewKey();
        byte[] swappedKey = other.ExportSubjectPublicKeyInfo();

        Assert.False(OutpostPairingProof.Check(Secret, Sample.Agent, swappedKey, Proof));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_proof_made_for_another_identity_is_refused()
    {
        Assert.False(OutpostPairingProof.Check(Secret, Sample.OtherAgent, Sample.PublicKey, Proof));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void A_proof_that_is_not_32_bytes_is_refused_and_throws_nothing(int length)
    {
        // The right proof cut short, or with bytes after it.
        byte[] proof = new byte[length];
        Proof.AsSpan(0, Math.Min(length, Proof.Length)).CopyTo(proof);

        Assert.False(OutpostPairingProof.Check(Secret, Sample.Agent, Sample.PublicKey, proof));
    }
}
