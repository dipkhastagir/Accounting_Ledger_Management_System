using Microsoft.AspNetCore.Mvc;
using TroyeeLedger.Services;

namespace TroyeeLedger.Controllers;

public class AnalyticsController : BaseController
{
    private readonly IAnalyticsService _analytics;
    public AnalyticsController(IAnalyticsService analytics) => _analytics = analytics;

    public async Task<IActionResult> Ratios(DateTime? asOf) => View(await _analytics.RatiosAsync((asOf ?? DateTime.Today).Date));

    public async Task<IActionResult> Trends(DateTime? asOf) => View(await _analytics.TrendsAsync((asOf ?? DateTime.Today).Date));
}
