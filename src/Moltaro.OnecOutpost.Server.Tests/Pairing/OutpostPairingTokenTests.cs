namespace Moltaro.OnecOutpost.Server.Tests.Pairing;

// The constructor of OutpostPairingToken, with which an application fakes IOutpostServer in its own tests.
public sealed class OutpostPairingTokenTests
{
    private const string Secret = "token-with-its-secret-inside";

    private static readonly Guid AgentId = Guid.Parse("5b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0");

    private static readonly DateTimeOffset ExpiresAt = new(2026, 11, 20, 12, 15, 0, TimeSpan.Zero);

    [Fact]
    public void A_token_keeps_its_agent_identity_its_text_and_its_expiry()
    {
        OutpostPairingToken token = new(AgentId, Secret, ExpiresAt);

        Assert.Equal(AgentId, token.AgentId);
        Assert.Equal(Secret, token.Token);
        Assert.Equal(ExpiresAt, token.ExpiresAt);
    }

    [Fact]
    public void An_empty_agent_identity_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new OutpostPairingToken(Guid.Empty, Secret, ExpiresAt));
    }

    [Fact]
    public void A_null_token_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new OutpostPairingToken(AgentId, null!, ExpiresAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void An_empty_or_white_space_token_is_refused(string text)
    {
        Assert.Throws<ArgumentException>(() => new OutpostPairingToken(AgentId, text, ExpiresAt));
    }

    // The expiry is shown in a form the contract does not fix, so the test only tells two expiries apart.
    [Fact]
    public void The_text_of_a_token_gives_its_agent_identity_and_its_expiry_but_never_the_token()
    {
        string text = new OutpostPairingToken(AgentId, Secret, ExpiresAt).ToString();
        string later = new OutpostPairingToken(AgentId, Secret, ExpiresAt.AddDays(1)).ToString();

        Assert.Contains(AgentId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(text, later);
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
    }
}
