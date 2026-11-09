namespace Bigarr.Api.Infrastructure.Xem;

public static class XemExtensions
{
    public static IServiceCollection AddBigarrXem(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<XemOptions>(configuration.GetSection(XemOptions.Section));
        var options = configuration.GetSection(XemOptions.Section).Get<XemOptions>() ?? new XemOptions();
        services.AddHttpClient<IXemClient, XemClient>(http =>
        {
            http.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Bigarr/0.1 (+https://github.com/Hezflix/Bigarr)");
        });
        services.AddSingleton<XemMappedIds>();
        services.AddScoped<XemService>();
        return services;
    }
}
