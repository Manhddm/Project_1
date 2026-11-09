using SmartMedBox.Infrastructure.Security;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.UnitTests.Security;

public sealed class InMemoryUserSessionStoreTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));
    private readonly InMemoryUserSessionStore _store;

    public InMemoryUserSessionStoreTests()
    {
        _store = new InMemoryUserSessionStore(_clock);
    }

    [Fact]
    public void Create_ReturnsActiveSession()
    {
        var sessionId = _store.Create(UserId, _clock.UtcNow.AddHours(1));

        Assert.True(_store.IsActive(sessionId, UserId));
    }

    [Fact]
    public void Create_ReturnsUniqueSessionIds()
    {
        var first = _store.Create(UserId, _clock.UtcNow.AddHours(1));
        var second = _store.Create(UserId, _clock.UtcNow.AddHours(1));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void IsActive_WrongUser_ReturnsFalse()
    {
        var sessionId = _store.Create(UserId, _clock.UtcNow.AddHours(1));

        Assert.False(_store.IsActive(sessionId, Guid.NewGuid()));
    }

    [Fact]
    public void IsActive_UnknownSession_ReturnsFalse()
    {
        Assert.False(_store.IsActive("khong-ton-tai", UserId));
    }

    [Fact]
    public void Revoke_DeactivatesSession()
    {
        var sessionId = _store.Create(UserId, _clock.UtcNow.AddHours(1));

        _store.Revoke(sessionId);

        Assert.False(_store.IsActive(sessionId, UserId));
    }

    [Fact]
    public void IsActive_AtExpiry_ReturnsFalse()
    {
        var expiresAt = _clock.UtcNow.AddMinutes(30);
        var sessionId = _store.Create(UserId, expiresAt);

        _clock.UtcNow = expiresAt;

        Assert.False(_store.IsActive(sessionId, UserId));
    }

    [Fact]
    public void RevokeAllForUser_DeactivatesOnlyThatUsersSessions()
    {
        var otherUser = Guid.NewGuid();
        var first = _store.Create(UserId, _clock.UtcNow.AddHours(1));
        var second = _store.Create(UserId, _clock.UtcNow.AddHours(1));
        var other = _store.Create(otherUser, _clock.UtcNow.AddHours(1));

        _store.RevokeAllForUser(UserId);

        Assert.False(_store.IsActive(first, UserId));
        Assert.False(_store.IsActive(second, UserId));
        Assert.True(_store.IsActive(other, otherUser));
    }
}
