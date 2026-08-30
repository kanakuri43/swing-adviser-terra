using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class PositionCorporateActionAdjustmentConfiguration : IEntityTypeConfiguration<PositionCorporateActionAdjustment>
{
    public void Configure(EntityTypeBuilder<PositionCorporateActionAdjustment> builder)
    {
        builder.ToTable("position_corporate_action_adjustments");
        builder.HasKey(entity => entity.AdjustmentId);
        builder.HasIndex(entity => new { entity.MarginLotId, entity.CorporateActionId }).IsUnique();
        builder.Property(entity => entity.Ratio).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.QuantityBefore).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.QuantityAfter).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CostBasisBefore).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CostBasisAfter).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AtrBasisBefore).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AtrBasisAfter).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.StopPriceBefore).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.StopPriceAfter).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.TakeProfitPriceBefore).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.TakeProfitPriceAfter).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AppliedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.MarginLot).WithMany(entity => entity.CorporateActionAdjustments).HasForeignKey(entity => entity.MarginLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.CorporateAction).WithMany().HasForeignKey(entity => entity.CorporateActionId).OnDelete(DeleteBehavior.Restrict);
    }
}
