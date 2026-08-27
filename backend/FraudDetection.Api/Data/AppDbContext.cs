using FraudDetection.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FraudDetection.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<FraudAlert> FraudAlerts => Set<FraudAlert>();
    public DbSet<CustomerRiskProfile> CustomerRiskProfiles => Set<CustomerRiskProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.ToTable("transactions");
            entity.HasIndex(t => t.OccurredAtUtc);
            entity.HasIndex(t => t.AccountId);
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<FraudAlert>(entity =>
        {
            entity.ToTable("fraud_alerts");
            entity.HasIndex(a => a.CreatedAtUtc);
            entity.Property(a => a.Severity).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(a => a.Transaction)
                  .WithMany(t => t.FraudAlerts)
                  .HasForeignKey(a => a.TransactionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerRiskProfile>(entity =>
        {
            entity.ToTable("customer_risk_profiles");
        });
    }
}
