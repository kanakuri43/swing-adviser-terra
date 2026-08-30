using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class TradeExecutionLotAllocationConfiguration : IEntityTypeConfiguration<TradeExecutionLotAllocation>
{
    public void Configure(EntityTypeBuilder<TradeExecutionLotAllocation> builder)
    {
        builder.ToTable("trade_execution_lot_allocations");
        builder.HasKey(entity => entity.AllocationId);
        builder.HasIndex(entity => new { entity.TradeExecutionId, entity.MarginLotId, entity.Revision }).IsUnique();
        builder.Property(entity => entity.Quantity).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.EffectiveAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.RecordedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.TradeExecution).WithMany(entity => entity.LotAllocations).HasForeignKey(entity => entity.TradeExecutionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.MarginLot).WithMany(entity => entity.TradeExecutionLotAllocations).HasForeignKey(entity => entity.MarginLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Supersedes).WithMany().HasForeignKey(entity => entity.SupersedesId).OnDelete(DeleteBehavior.Restrict);
    }
}
