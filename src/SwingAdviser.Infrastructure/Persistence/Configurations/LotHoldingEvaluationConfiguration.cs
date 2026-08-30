using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class LotHoldingEvaluationConfiguration : IEntityTypeConfiguration<LotHoldingEvaluation>
{
    public void Configure(EntityTypeBuilder<LotHoldingEvaluation> builder)
    {
        builder.ToTable("lot_holding_evaluations");
        builder.HasKey(entity => entity.LotEvaluationId);
        builder.HasIndex(entity => new { entity.MarginLotId, entity.EvaluationBarDate, entity.EvaluatedAtUtc }).IsUnique();
        builder.Property(entity => entity.EvaluationBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.EvaluatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.StopReachedToday).HasColumnType("INTEGER");
        builder.Property(entity => entity.TargetReachedToday).HasColumnType("INTEGER");
        builder.Property(entity => entity.PriorTargetFirstReachBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.PartialExitCandidateQuantity).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CreatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.MarginLot).WithMany().HasForeignKey(entity => entity.MarginLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Position).WithMany().HasForeignKey(entity => entity.PositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.DailyBar).WithMany().HasForeignKey(entity => entity.DailyBarId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.RiskPlanRevision).WithMany().HasForeignKey(entity => entity.RiskPlanRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}
