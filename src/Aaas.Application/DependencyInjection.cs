using Aaas.Application.Accounts;
using Aaas.Application.Events;
using Aaas.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Aaas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AccountService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<EventOccupancyReader>();
        services.AddScoped<AdminEventService>();
        services.AddScoped<PublicEventService>();
        return services;
    }
}
