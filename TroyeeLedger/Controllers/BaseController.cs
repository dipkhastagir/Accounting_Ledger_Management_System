using Microsoft.AspNetCore.Mvc;
using TroyeeLedger.Helpers;

namespace TroyeeLedger.Controllers;

public abstract class BaseController : Controller
{
    protected void Success(string message) => TempData["Success"] = message;
    protected void Error(string message) => TempData["Error"] = message;

    /// <summary>Default reporting window: start of the Bangladesh fiscal year (1 July) to today.</summary>
    protected static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to)
    {
        var t = (to ?? DateTime.Today).Date;
        var fyStart = new DateTime(t.Month >= 7 ? t.Year : t.Year - 1, 7, 1);
        var f = (from ?? fyStart).Date;
        if (f > t) (f, t) = (t, f);
        return (f, t);
    }

    protected FileContentResult Csv(CsvBuilder csv, string fileName) =>
        File(csv.ToBytes(), "text/csv", $"{fileName}-{DateTime.Now:yyyyMMdd-HHmm}.csv");

    protected bool IsCsv(string? export) => string.Equals(export, "csv", StringComparison.OrdinalIgnoreCase);
}
