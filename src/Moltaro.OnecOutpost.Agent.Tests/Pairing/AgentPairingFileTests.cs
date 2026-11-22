using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Pairing;

// The form of agent.json (the S6a brief): {"server":…,"agent":…,"serverKey":…,"pollTimeoutMs":…,"leaseGraceMs":…} in the
// forms of §2 of docs/protocol.md: an absolute http or https URL, an identity, the 91-byte server key in base64url, and
// the two durations in the ranges of the options PollTimeout and LeaseGrace, from 1 second to 5 minutes. Read takes
// exactly these members and refuses anything else with FormatException, quoting nothing of the file. From S6c on, it
// also refuses an address that the transport's rule of §11 refuses (ServerRoute.IsAllowed), so a start records it.
public sealed class AgentPairingFileTests
{
    private static readonly Guid Agent = Guid.Parse("5f0c3a1e-2b4d-4c6e-8a10-b2c3d4e5f607");

    private readonly byte[] _serverKey;

    public AgentPairingFileTests()
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        _serverKey = server.ExportSubjectPublicKeyInfo();
    }

    [Fact]
    public void The_settings_are_written_in_the_form_of_the_file_by_the_writer_of_section_2()
    {
        AgentPairingSettings settings = new(
            new Uri("https://outpost.example.com/agents"),
            Agent,
            _serverKey,
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(10));

        string expected =
            $$"""{"server":"https://outpost.example.com/agents","agent":"{{Agent}}","serverKey":"{{Base64Url.EncodeToString(_serverKey)}}","pollTimeoutMs":25000,"leaseGraceMs":10000}""";

        Assert.Equal(expected, Encoding.UTF8.GetString(AgentPairingFile.Write(settings)));
    }

    [Theory]
    [InlineData("https://outpost.example.com/agents", 25_000, 10_000)]
    [InlineData("http://127.0.0.1:5000/agents", 1_000, 1_000)]
    [InlineData("https://outpost.example.com/agents", 300_000, 300_000)]
    public void The_settings_read_back_as_they_were_written(string server, int pollTimeoutMs, int leaseGraceMs)
    {
        AgentPairingSettings settings = new(
            new Uri(server),
            Agent,
            _serverKey,
            TimeSpan.FromMilliseconds(pollTimeoutMs),
            TimeSpan.FromMilliseconds(leaseGraceMs));

        AgentPairingSettings read = AgentPairingFile.Read(AgentPairingFile.Write(settings));

        Assert.Equal(settings.Server, read.Server);
        Assert.Equal(settings.Agent, read.Agent);
        Assert.Equal(settings.ServerKey, read.ServerKey);
        Assert.Equal(settings.PollTimeout, read.PollTimeout);
        Assert.Equal(settings.LeaseGrace, read.LeaseGrace);
    }

    // A reader of §2 accepts insignificant white space and any order of the members.
    [Fact]
    public void A_file_with_white_space_and_its_members_in_another_order_is_read()
    {
        string file = $$"""
            {
              "leaseGraceMs": 10000,
              "pollTimeoutMs": 25000,
              "serverKey": "{{Base64Url.EncodeToString(_serverKey)}}",
              "agent": "{{Agent}}",
              "server": "https://outpost.example.com/agents"
            }
            """;

        AgentPairingSettings read = AgentPairingFile.Read(Encoding.UTF8.GetBytes(file));

        Assert.Equal(new Uri("https://outpost.example.com/agents"), read.Server);
        Assert.Equal(Agent, read.Agent);
        Assert.Equal(_serverKey, read.ServerKey);
        Assert.Equal(TimeSpan.FromSeconds(25), read.PollTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), read.LeaseGrace);
    }

    // Each case changes one member of a valid file, or the file as a whole; S3cret stands where a value of the file is,
    // which the error must not quote. KEY is the server key in base64url.
    [Theory]
    [InlineData("S3cret is not JSON")]
    [InlineData("""["S3cret"]""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":10000,"note":"S3cret"}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":null,"agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"/S3cret/agents","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"ftp://S3cret.example.com/agents","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"S3cret","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"5F0C3A1E-2B4D-4C6E-8A10-B2C3D4E5F607","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"S3cret!","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"SHORTKEY","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"NOKEY","pollTimeoutMs":25000,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":999,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":300001,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000.5,"leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":"25000","leaseGraceMs":10000}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":999}""")]
    [InlineData("""{"server":"https://S3cret.example.com/","agent":"AGENT","serverKey":"KEY","pollTimeoutMs":25000,"leaseGraceMs":300001}""")]
    public void A_file_that_breaks_its_form_fails_with_FormatException_quoting_nothing(string content)
    {
        // SHORTKEY is a key of 90 bytes, and NOKEY 91 bytes that are not a key of P-256 (§2, "Public keys").
        string file = content
            .Replace("AGENT", Agent.ToString(), StringComparison.Ordinal)
            .Replace("SHORTKEY", Base64Url.EncodeToString(_serverKey.AsSpan(0, 90)), StringComparison.Ordinal)
            .Replace("NOKEY", Base64Url.EncodeToString(new byte[91]), StringComparison.Ordinal)
            .Replace("KEY", Base64Url.EncodeToString(_serverKey), StringComparison.Ordinal);

        FormatException exception = Assert.ThrowsAny<FormatException>(
            () => AgentPairingFile.Read(Encoding.UTF8.GetBytes(file)));

        Assert.DoesNotContain("S3cret", exception.Message, StringComparison.Ordinal);
    }

    // §11, from S6c: http:// only on a loopback address, as Uri writes its host.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("http://outpost.example.com/agents")]
    [InlineData("http://localhost./agents")]
    [InlineData("http://192.0.2.10:5000/agents")]
    public void An_address_that_section_11_refuses_fails_with_FormatException_quoting_nothing(string server)
    {
        string file =
            $$"""{"server":"{{server}}","agent":"{{Agent}}","serverKey":"{{Base64Url.EncodeToString(_serverKey)}}","pollTimeoutMs":25000,"leaseGraceMs":10000}""";

        FormatException exception = Assert.ThrowsAny<FormatException>(
            () => AgentPairingFile.Read(Encoding.UTF8.GetBytes(file)));

        Assert.DoesNotContain(new Uri(server).Host, exception.Message, StringComparison.Ordinal);
    }
}
