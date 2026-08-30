using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class RiskPlanConfiguration : IEntityTypeConfiguration<RiskPlan>
{
    public void Configure(EntityTypeBuilder<RiskPlan> builder)
    {
        builder.ToTable("risk_plans");
        builder.HasKey(entity => entity.RiskPlanId);
        builder.HasIndex(entity => new { entity.MarginLotId, entity.Revision }).IsUnique();
        builder.Property(entity => entity.StopPrice).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.TakeProfitPrice).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.PartialTakeProfitFraction).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.EffectiveAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.RecordedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.MarginLot).WithMany().HasForeignKey(entity => entity.MarginLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.RiskBasis).WithMany().HasForeignKey(entity => entity.RiskBasisId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.TriggerTradeExecution).WithMany().HasForeignKey(entity => entity.TriggerTradeExecutionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.TriggerAllocation).WithMany().HasForeignKey(entity => entity.TriggerAllocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.SupersedesRevision).WithMany().HasForeignKey(entity => entity.SupersedesRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}
