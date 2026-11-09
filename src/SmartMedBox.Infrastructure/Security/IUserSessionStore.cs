namespace SmartMedBox.Infrastructure.Security;

public interface IUserSessionStore
{
    string Create(Guid userId, DateTimeOffset expiresAt);

    bool IsActive(string sessionId, Guid userId);

    void Revoke(string sessionId);

    void RevokeAllForUser(Guid userId);
}
