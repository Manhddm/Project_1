using System.Collections.Concurrent;
using System.Security.Cryptography;
using SmartMedBox.Domain.Abstractions;

namespace SmartMedBox.Infrastructure.Security;

public sealed class InMemoryUserSessionStore(IClock clock) : IUserSessionStore
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);

    public string Create(Guid userId, DateTimeOffset expiresAt)
    {
        var sessionId = NewSessionId();
        _sessions[sessionId] = new SessionEntry(userId, expiresAt);
        return sessionId;
    }

    public bool IsActive(string sessionId, Guid userId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry))
        {
            return false;
        }

        if (entry.UserId != userId || entry.ExpiresAt <= clock.UtcNow)
        {
            _sessions.TryRemove(sessionId, out _);
            return false;
        }

        return true;
    }

    public void Revoke(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public void RevokeAllForUser(Guid userId)
    {
        foreach (var (sessionId, entry) in _sessions)
        {
            if (entry.UserId == userId)
            {
                _sessions.TryRemove(sessionId, out _);
            }
        }
    }

    private static string NewSessionId() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private sealed record SessionEntry(Guid UserId, DateTimeOffset ExpiresAt);
}
