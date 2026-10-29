using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartMedBox.Infrastructure.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Design-time only: SMARTMEDBOX_CONNECTION overrides the local default used for `dotnet ef`.
        var connectionString = Environment.GetEnvironmentVariable("SMARTMEDBOX_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=SmartMedBox;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
