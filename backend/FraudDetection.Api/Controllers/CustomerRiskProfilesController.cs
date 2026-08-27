using FraudDetection.Api.Data;
using FraudDetection.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FraudDetection.Api.Controllers;

/// <summary>Read-only view onto behavioral baselines the PySpark analytics job computes
/// from transaction history and writes directly to Postgres.</summary>
[ApiController]
[Route("api/customer-risk-profiles")]
public class CustomerRiskProfilesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CustomerRiskProfilesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("{accountId:guid}")]
    public async Task<ActionResult<CustomerRiskProfileDto>> GetProfile(Guid accountId, CancellationToken ct)
    {
        var profile = await _db.CustomerRiskProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId, ct);
        return profile is null ? NotFound() : Ok(CustomerRiskProfileDto.FromEntity(profile));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerRiskProfileDto>>> GetProfiles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.CustomerRiskProfiles.OrderByDescending(p => p.UpdatedAtUtc);
        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => CustomerRiskProfileDto.FromEntity(p))
            .ToListAsync(ct);

        return Ok(new PagedResult<CustomerRiskProfileDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount });
    }
}
