namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Xem;
using Microsoft.Extensions.Options;

/// <summary>The slow schedule for TheXEM: scene names and every series' scene numbering, twice a day by default.</summary>
public sealed class SceneMappingRefreshJob(XemService xem, IOptions<XemOptions> options) : IScheduledJob
{
    public const string JobName = "sceneMappingRefresh";

    public string Name => JobName;

    public string DisplayName => "Scene mapping refresh (TheXEM)";

    public TimeSpan Interval => TimeSpan.FromHours(Math.Max(1, options.Value.RefreshIntervalHours));

    public bool Enabled => options.Value.Enabled;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(5);

    /// <summary>A pass over the whole library: its own lane.</summary>
    public bool LongRunning => true;

    public Task<string?> RunAsync(CancellationToken ct) => xem.RefreshAllAsync(ct)!;
}
