using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Integrations;

public static class IntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddTingee(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TingeeOptions>(configuration.GetSection(TingeeOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITingeeWebhookInbox, UnavailableTingeeWebhookInbox>();
        // Every outgoing Tingee call is recorded in dbo.TingeeApiLogs (migration 014) when a database is configured.
        if (string.IsNullOrWhiteSpace(configuration["Database:ConnectionString"])) services.TryAddSingleton<ITingeeCallLog, NullTingeeCallLog>();
        else services.TryAddSingleton<ITingeeCallLog, SqlTingeeCallLog>();
        services.AddHttpClient<TingeeClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.TryAddScoped<ITingeeApi, TingeeApi>();
        services.TryAddScoped<ITingeeInvoiceApi, TingeeInvoiceApi>();
        return services;
    }
}
