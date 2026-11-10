using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parakeet.Application;
using Parakeet.Application.Auth;
using Parakeet.Infrastructure.Auth;
using Parakeet.Infrastructure.Persistence;

namespace Parakeet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        // "What time is it" always goes through TimeProvider so tests can set the clock.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        return services;
    }
}
