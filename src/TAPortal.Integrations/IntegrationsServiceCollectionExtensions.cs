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
        services.AddHttpClient<TingeeClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
        return services;
    }
}
