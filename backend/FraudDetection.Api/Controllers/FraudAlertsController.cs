using FraudDetection.Api.Data;
using FraudDetection.Api.DTOs;
using FraudDetection.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FraudDetection.Api.Controllers;

[ApiController]
[Route("api/fraud-alerts")]
public class FraudAlertsController : ControllerBase
{
    private readonly AppDbContext _db;

    public FraudAlertsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Paginated, filterable alert list. `source` is "ScalaRiskEngine" or "PySparkAnomalyDetection".</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<FraudAlertDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<FraudAlertDto>>> GetAlerts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] AlertSeverity? severity = null,
        [FromQuery] AlertStatus? status = null,
        [FromQuery] string? source = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? transactionId = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.FraudAlerts.Include(a => a.Transaction).AsQueryable();
        if (severity is not null) query = query.Where(a => a.Severity == severity);
        if (status is not null) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(a => a.Source == source);
        if (transactionId is not null) query = query.Where(a => a.TransactionId == transactionId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = search.ToLower();
            query = query.Where(a => a.Transaction!.MerchantName.ToLower().Contains(pattern));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => FraudAlertDto.FromEntity(a))
            .ToListAsync(ct);

        return Ok(new PagedResult<FraudAlertDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FraudAlertDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FraudAlertDto>> GetAlert(Guid id, CancellationToken ct)
    {
        var alert = await _db.FraudAlerts.Include(a => a.Transaction).FirstOrDefaultAsync(a => a.Id == id, ct);
        return alert is null ? NotFound() : Ok(FraudAlertDto.FromEntity(alert));
    }

    /// <summary>Top triggered rule names across all alerts, most frequent first -- backs the
    /// Angular dashboard's "top fraud triggers" panel.</summary>
    [HttpGet("top-triggers")]
    [ProducesResponseType(typeof(List<TopTriggerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TopTriggerDto>>> GetTopTriggers([FromQuery] int limit = 5, CancellationToken ct = default)
    {
        var rows = await _db.FraudAlerts
            .Where(a => a.TriggeredRules != null)
            .Select(a => a.TriggeredRules!)
            .ToListAsync(ct);

        var counts = new Dictionary<string, int>();
        foreach (var json in rows)
        {
            foreach (var rule in System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new())
            {
                counts[rule] = counts.GetValueOrDefault(rule) + 1;
            }
        }

        var top = counts
            .OrderByDescending(kv => kv.Value)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(kv => new TopTriggerDto { RuleName = kv.Key, Count = kv.Value })
            .ToList();

        return Ok(top);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize]
    [ProducesResponseType(typeof(FraudAlertDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FraudAlertDto>> UpdateStatus(Guid id, [FromBody] AlertStatus status, CancellationToken ct)
    {
        var alert = await _db.FraudAlerts.Include(a => a.Transaction).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null) return NotFound();

        alert.Status = status;
        await _db.SaveChangesAsync(ct);

        return Ok(FraudAlertDto.FromEntity(alert));
    }
}
