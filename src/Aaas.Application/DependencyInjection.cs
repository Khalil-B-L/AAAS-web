using Aaas.Application.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace Aaas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AccountService>();
        return services;
    }
}
