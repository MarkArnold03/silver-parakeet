using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Parakeet.Infrastructure.Persistence;

/// <summary>
/// Used only by the "dotnet ef" command line tool (creating migrations). The running app
/// builds its DbContext in DependencyInjection.cs instead.
/// </summary>
internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=Parakeet;User Id=sa;Password=Parakeet_dev_1;TrustServerCertificate=True";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
    }
}
