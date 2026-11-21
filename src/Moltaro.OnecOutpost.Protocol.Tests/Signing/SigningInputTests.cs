using System.Security.Cryptography;
using System.Text;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Signing;

// The signing inputs of §3 of docs/protocol.md, as known values: each input is written out here in full, with hashes
// computed outside the codec, and the signatures are made and checked by .NET alone on one side of every test. A
// signature is ECDSA P-256 with SHA-256 as the 64 bytes r ‖ s (§2).
public sealed class SigningInputTests : IDisposable
{
    // SHA-256 over these bytes is 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e.
    private static readonly byte[] RequestBody = "{\"number\":1}"u8.ToArray();

    // SHA-256 over these bytes is 658c369feb0767db91d307b27905d57528b7cd112d84027216231fdbf8e0e76f.
    private static readonly byte[] AnswerBody = "{\"time\":\"2026-11-20T10:15:00.123Z\",\"code\":\"form\"}"u8.ToArray();

    private const string CommandInput =
        "outpost/1 command 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e";

    private readonly ECDsa _key = Sample.NewKey();

    public void Dispose() => _key.Dispose();

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("Pair", "outpost/1 request pair 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e")]
    [InlineData("Poll", "outpost/1 request poll 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e")]
    [InlineData("Outcome", "outpost/1 request outcome 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e")]
    public void A_request_is_signed_over_its_kind_its_agent_and_the_hash_of_its_body(string kind, string input)
    {
        byte[] signature = OutpostSignatures.SignRequest(_key, Kind(kind), Sample.Agent, RequestBody);

        Assert.Equal(64, signature.Length);
        Assert.True(VerifiesOutsideTheCodec(input, signature));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("Pair", 200, "outpost/1 answer pair 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b 200 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e 658c369feb0767db91d307b27905d57528b7cd112d84027216231fdbf8e0e76f")]
    [InlineData("Poll", 400, "outpost/1 answer poll 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b 400 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e 658c369feb0767db91d307b27905d57528b7cd112d84027216231fdbf8e0e76f")]
    [InlineData("Outcome", 413, "outpost/1 answer outcome 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b 413 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e 658c369feb0767db91d307b27905d57528b7cd112d84027216231fdbf8e0e76f")]
    public void An_answer_is_signed_over_its_kind_its_agent_its_status_and_the_hashes_of_the_request_and_of_its_body(
        string kind,
        int status,
        string input)
    {
        byte[] signature = OutpostSignatures.SignAnswer(_key, Kind(kind), Sample.Agent, status, RequestBody, AnswerBody);

        Assert.Equal(64, signature.Length);
        Assert.True(VerifiesOutsideTheCodec(input, signature));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_command_is_signed_over_the_hash_of_its_exact_bytes()
    {
        byte[] signature = OutpostSignatures.SignCommand(_key, RequestBody);

        Assert.Equal(64, signature.Length);
        Assert.True(VerifiesOutsideTheCodec(CommandInput, signature));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_signature_made_outside_the_codec_over_the_input_of_a_request_verifies()
    {
        byte[] signature = SignOutsideTheCodec(
            "outpost/1 request poll 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e");

        Assert.True(OutpostSignatures.VerifyRequest(_key, OutpostRequestKindEnum.Poll, Sample.Agent, RequestBody, signature));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_signature_made_outside_the_codec_over_the_input_of_an_answer_verifies()
    {
        byte[] signature = SignOutsideTheCodec(
            "outpost/1 answer outcome 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b 413 3d6a916e86efba9105154ea95dda8d8da47df173d931c23e7a346c7babd81a3e 658c369feb0767db91d307b27905d57528b7cd112d84027216231fdbf8e0e76f");

        Assert.True(OutpostSignatures.VerifyAnswer(
            _key,
            OutpostRequestKindEnum.Outcome,
            Sample.Agent,
            413,
            RequestBody,
            AnswerBody,
            signature));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_signature_made_outside_the_codec_over_the_input_of_a_command_verifies()
    {
        byte[] signature = SignOutsideTheCodec(CommandInput);

        Assert.True(OutpostSignatures.VerifyCommand(_key, RequestBody, signature));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    public void A_signature_that_is_not_64_bytes_does_not_verify_and_throws_nothing(int length)
    {
        byte[] signature = new byte[length];

        Assert.False(OutpostSignatures.VerifyCommand(_key, RequestBody, signature));
        Assert.False(OutpostSignatures.VerifyRequest(_key, OutpostRequestKindEnum.Poll, Sample.Agent, RequestBody, signature));
        Assert.False(OutpostSignatures.VerifyAnswer(
            _key,
            OutpostRequestKindEnum.Poll,
            Sample.Agent,
            200,
            RequestBody,
            AnswerBody,
            signature));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_signature_in_the_DER_form_does_not_verify()
    {
        byte[] signature = _key.SignData(
            Encoding.ASCII.GetBytes(CommandInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        Assert.False(OutpostSignatures.VerifyCommand(_key, RequestBody, signature));
    }

    private static OutpostRequestKindEnum Kind(string kind)
    {
        return Enum.Parse<OutpostRequestKindEnum>(kind);
    }

    private bool VerifiesOutsideTheCodec(string input, byte[] signature)
    {
        return _key.VerifyData(
            Encoding.ASCII.GetBytes(input),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private byte[] SignOutsideTheCodec(string input)
    {
        return _key.SignData(
            Encoding.ASCII.GetBytes(input),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
