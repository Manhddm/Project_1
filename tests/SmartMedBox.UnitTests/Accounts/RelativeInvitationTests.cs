using SmartMedBox.Domain.Entities;

namespace SmartMedBox.UnitTests.Accounts;

public sealed class RelativeInvitationTests
{
    [Fact]
    public void InviteeEmail_IsTrimmedAndLowercased()
    {
        var invitation = new RelativeInvitation { InviteeEmail = "  Relative@Example.COM " };

        Assert.Equal("relative@example.com", invitation.InviteeEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InviteeEmail_Blank_Throws(string email)
    {
        var invitation = new RelativeInvitation();

        Assert.Throws<ArgumentException>(() => invitation.InviteeEmail = email);
    }
}
