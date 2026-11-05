namespace Bigarr.Api.Infrastructure.Events;

public static class EventsExtensions
{
    public static IServiceCollection AddBigarrEvents(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NotificationsOptions>(configuration.GetSection(NotificationsOptions.Section));
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<WebhookSignal>();
        services.AddSingleton<OutboxInterceptor>();
        services.AddSingleton<EventStream>();
        services.AddScoped<OutboxWriter>();
        services.AddHttpClient(WebhookClient.HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue($"{NotificationsOptions.Section}:TimeoutSeconds", 30)));
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Bigarr/{Features.Server.SystemEndpoints.Version}");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })

            // The factory's handlers log every request URI at Information, and a webhook URL can carry a token (Discord's does).
            .RemoveAllLoggers();
        services.AddSingleton<WebhookClient>();
        services.AddHostedService<OutboxDispatcher>();
        services.AddHostedService<WebhookSender>();
        return services;
    }
}
