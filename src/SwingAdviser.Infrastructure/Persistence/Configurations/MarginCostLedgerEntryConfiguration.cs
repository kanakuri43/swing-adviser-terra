using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class MarginCostLedgerEntryConfiguration : IEntityTypeConfiguration<MarginCostLedgerEntry>
{
    public void Configure(EntityTypeBuilder<MarginCostLedgerEntry> builder)
    {
        builder.ToTable("margin_cost_ledger_entries");
        builder.HasKey(entity => entity.LedgerEntryId);
        builder.HasIndex(entity => new { entity.MarginLotId, entity.CostType, entity.PeriodStart, entity.PeriodEnd, entity.Revision }).IsUnique();
        builder.Property(entity => entity.PeriodStart).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.PeriodEnd).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Quantity).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Amount).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Rate).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AvailableAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.ObservedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.RecordedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.MarginLot).WithMany().HasForeignKey(entity => entity.MarginLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Supersedes).WithMany().HasForeignKey(entity => entity.SupersedesId).OnDelete(DeleteBehavior.Restrict);
    }
}
