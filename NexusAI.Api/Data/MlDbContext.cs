using Microsoft.EntityFrameworkCore;
using NexusAI.Api.Data.Entities;

namespace NexusAI.Api.Data;

/// <summary>
/// 連接 mldatabase 的獨立 DbContext
/// （與 NexusAI 主 DB 分開，避免 Migration 混用）
/// </summary>
public class MlDbContext : DbContext
{
    public MlDbContext(DbContextOptions<MlDbContext> options) : base(options) { }

    public DbSet<QuotationEntity> Quotations => Set<QuotationEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<QuotationEntity>(e =>
        {
            e.ToTable("machinelearning_quatation");

            // Indexes for common query patterns
            e.HasIndex(q => q.QuoteName);
            e.HasIndex(q => q.CustomerName);
            e.HasIndex(q => q.CreatedAt);
            e.HasIndex(q => q.UpdatedAt);

            // Computed: total_cost stored in DB, EF reads it but does NOT update via formula
            e.Property(q => q.TotalCost)
             .HasColumnType("decimal(18,2)");
        });

    }
}
