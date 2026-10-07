using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Data;
using TroyeeLedger.Models.Enums;
using TroyeeLedger.Services;

namespace TroyeeLedger.Filters;

/// <summary>Supplies company name and the approval-queue badge to the shared layout.</summary>
public class LayoutDataFilter : IAsyncActionFilter
{
    private readonly AppDbContext _db;
    private readonly ISettingsService _settings;

    public LayoutDataFilter(AppDbContext db, ISettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.Controller is Controller c && context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            var s = await _settings.GetAsync();
            c.ViewData["CompanyName"] = s.CompanyName;
            c.ViewData["Currency"] = s.CurrencySymbol;
            c.ViewData["PendingCount"] = await _db.Vouchers.CountAsync(v => v.Status == VoucherStatus.PendingApproval);
        }
        await next();
    }
}
