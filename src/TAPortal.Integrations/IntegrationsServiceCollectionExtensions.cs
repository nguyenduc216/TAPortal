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
        services.TryAddScoped<TingeeTransactionSync>();
        // SoftDreams EasyInvoice (operator 2026-11-26), called directly; the company login is kept DPAPI-encrypted (machine
        // scope, so the Customer Portal and the webhook site's worker read the same value).
        services.Configure<SoftDreams.EasyInvoiceOptions>(configuration.GetSection(SoftDreams.EasyInvoiceOptions.SectionName));
        services.AddHttpClient<SoftDreams.IEasyInvoiceApi, SoftDreams.EasyInvoiceApi>(client => client.Timeout = TimeSpan.FromSeconds(90));
        if (OperatingSystem.IsWindows()) services.TryAddSingleton<SoftDreams.IInvoiceSecretProtector, SoftDreams.DpapiInvoiceSecretProtector>();
        return services;
    }
}
