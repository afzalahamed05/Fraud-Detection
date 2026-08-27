using FraudDetection.Api.Data;
using FraudDetection.Api.DTOs;
using FraudDetection.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FraudDetection.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats(CancellationToken ct)
    {
        var totalTransactions = await _db.Transactions.CountAsync(ct);
        var totalAlerts = await _db.FraudAlerts.CountAsync(ct);
        var openAlerts = await _db.FraudAlerts.CountAsync(a => a.Status == AlertStatus.Open, ct);
        var totalAmount = await _db.Transactions.SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var flaggedAmount = await _db.Transactions
            .Where(t => t.Status == TransactionStatus.Flagged)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0;

        var bySeverity = await _db.FraudAlerts
            .GroupBy(a => a.Severity)
            .Select(g => new { Severity = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        return Ok(new DashboardStatsDto
        {
            TotalTransactions = totalTransactions,
            TotalAlerts = totalAlerts,
            OpenAlerts = openAlerts,
            FraudRate = totalTransactions == 0 ? 0 : Math.Round((double)totalAlerts / totalTransactions * 100, 2),
            TotalAmount = totalAmount,
            FlaggedAmount = flaggedAmount,
            AlertsBySeverity = bySeverity.ToDictionary(x => x.Severity, x => x.Count)
        });
    }

    /// <summary>Daily transaction volume/fraud counts for the last `days` days -- backs the
    /// Angular dashboard's fraud-trend chart.</summary>
    [HttpGet("trends")]
    [ProducesResponseType(typeof(List<DailyTrendDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DailyTrendDto>>> GetTrends([FromQuery] int days = 14, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var transactions = await _db.Transactions
            .Where(t => t.OccurredAtUtc >= since)
            .Select(t => new { t.OccurredAtUtc, t.Status, t.Amount })
            .ToListAsync(ct);

        var byDay = transactions
            .GroupBy(t => t.OccurredAtUtc.Date)
            .ToDictionary(g => g.Key, g => new DailyTrendDto
            {
                Date = g.Key,
                TransactionCount = g.Count(),
                FlaggedCount = g.Count(t => t.Status == TransactionStatus.Flagged),
                TotalAmount = g.Sum(t => t.Amount)
            });

        var trend = Enumerable.Range(0, days)
            .Select(offset => since.AddDays(offset))
            .Select(date => byDay.TryGetValue(date, out var value) ? value : new DailyTrendDto { Date = date })
            .ToList();

        return Ok(trend);
    }
}
