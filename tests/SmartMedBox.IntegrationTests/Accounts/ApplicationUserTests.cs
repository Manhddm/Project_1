using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.IntegrationTests.Schema;

namespace SmartMedBox.IntegrationTests.Accounts;

public sealed class ApplicationUserTests(SqlServerDatabaseFixture fixture)
    : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task Save_DuplicateEmailDifferentCaseAndWhitespace_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        db.ApplicationUsers.Add(NewUser($"  {email.ToUpperInvariant()} "));
        await db.SaveChangesAsync();

        db.ApplicationUsers.Add(NewUser(email));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_StoresNormalizedEmailAndHashedPassword()
    {
        await using var db = fixture.CreateContext();
        var email = $"  user-{Guid.NewGuid():N}@Example.COM ";
        const string password = "P@ssw0rd!2026";
        var hasher = new PasswordHasherService();
        var user = NewUser(email);
        user.PasswordHash = hasher.Hash(password);

        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();

        var stored = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        Assert.Equal(email.Trim().ToLowerInvariant(), stored.Email);
        Assert.NotEqual(password, stored.PasswordHash);
        Assert.True(hasher.Verify(stored.PasswordHash, password));
        Assert.False(hasher.Verify(stored.PasswordHash, "wrong-password"));
    }

    private static ApplicationUser NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = "not-a-real-hash",
        EmailVerified = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
