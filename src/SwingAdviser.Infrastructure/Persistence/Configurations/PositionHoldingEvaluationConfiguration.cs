using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class PositionHoldingEvaluationConfiguration : IEntityTypeConfiguration<PositionHoldingEvaluation>
{
    public void Configure(EntityTypeBuilder<PositionHoldingEvaluation> builder)
    {
        builder.ToTable("position_holding_evaluations");
        builder.HasKey(entity => entity.PositionEvaluationId);
        builder.HasIndex(entity => new { entity.PositionId, entity.EvaluationBarDate, entity.EvaluatedAtUtc }).IsUnique();
        builder.Property(entity => entity.EvaluationBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.EvaluatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.PartialExitTotalCandidateQuantity).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CreatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.Position).WithMany().HasForeignKey(entity => entity.PositionId).OnDelete(DeleteBehavior.Restrict);
    }
}
