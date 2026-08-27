using FraudDetection.Api.Data;
using FraudDetection.Api.DTOs;
using FraudDetection.Api.Messaging;
using FraudDetection.Api.Models;
using FraudDetection.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FraudDetection.Api.Controllers;

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly KafkaProducerService _producer;

    public TransactionsController(AppDbContext db, KafkaProducerService producer)
    {
        _db = db;
        _producer = producer;
    }

    /// <summary>Paginated, filterable transaction list. `search` matches merchant name (case-insensitive, partial).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TransactionDto>>> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] TransactionStatus? status = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Transactions.Include(t => t.FraudAlerts).AsQueryable();
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }
        if (accountId is not null)
        {
            query = query.Where(t => t.AccountId == accountId);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = search.ToLower();
            query = query.Where(t => t.MerchantName.ToLower().Contains(pattern));
        }
        if (fromUtc is not null)
        {
            query = query.Where(t => t.OccurredAtUtc >= fromUtc);
        }
        if (toUtc is not null)
        {
            query = query.Where(t => t.OccurredAtUtc <= toUtc);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => TransactionDto.FromEntity(t))
            .ToListAsync(ct);

        return Ok(new PagedResult<TransactionDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> GetTransaction(Guid id, CancellationToken ct)
    {
        var transaction = await _db.Transactions
            .Include(t => t.FraudAlerts)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (transaction is null)
        {
            return NotFound();
        }

        return Ok(TransactionDto.FromEntity(transaction));
    }

    /// <summary>
    /// Persists the transaction as Pending, then hands it to Kafka for async fraud scoring
    /// (the Scala Structured Streaming risk engine in spark-jobs/scala-risk-engine). The row
    /// is durable before publish is even attempted, so a Kafka outage delays scoring but
    /// never loses the transaction — the outbox sweep (TransactionOutboxService) retries
    /// anything left with PublishedToKafkaUtc == null.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TransactionDto>> CreateTransaction(CreateTransactionDto dto, CancellationToken ct)
    {
        var transaction = new Transaction
        {
            AccountId = dto.AccountId,
            MerchantName = dto.MerchantName,
            MerchantCategory = dto.MerchantCategory,
            Amount = dto.Amount,
            Currency = dto.Currency,
            CountryCode = dto.CountryCode,
            OccurredAtUtc = dto.OccurredAtUtc ?? DateTime.UtcNow,
            Status = TransactionStatus.Pending
        };

        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync(ct);

        var published = await _producer.PublishTransactionCreatedAsync(new TransactionCreatedEventV1
        {
            TransactionId = transaction.Id,
            AccountId = transaction.AccountId,
            MerchantName = transaction.MerchantName,
            MerchantCategory = transaction.MerchantCategory,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            CountryCode = transaction.CountryCode,
            OccurredAtUtc = transaction.OccurredAtUtc
        }, ct);

        if (published)
        {
            transaction.PublishedToKafkaUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return CreatedAtAction(nameof(GetTransaction), new { id = transaction.Id }, TransactionDto.FromEntity(transaction));
    }
}
