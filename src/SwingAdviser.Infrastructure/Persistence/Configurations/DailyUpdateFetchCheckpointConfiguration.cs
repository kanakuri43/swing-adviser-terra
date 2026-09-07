using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class DailyUpdateFetchCheckpointConfiguration : IEntityTypeConfiguration<DailyUpdateFetchCheckpoint>
{
    public void Configure(EntityTypeBuilder<DailyUpdateFetchCheckpoint> builder)
    {
        builder.ToTable("daily_update_fetch_checkpoints");
        builder.HasKey(entity => entity.DailyUpdateFetchCheckpointId);
        builder.HasIndex(entity => new { entity.EvaluationBarDate, entity.SourceKind, entity.TargetKey, entity.CompletedAtUtc })
            .HasDatabaseName("ix_daily_update_fetch_checkpoints_resume_lookup");
        builder.HasIndex(entity => entity.DailyUpdateRunId);
        builder.HasIndex(entity => entity.InstrumentId);
        builder.Property(entity => entity.EvaluationBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.RequestedRangeStartDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CoveredThroughDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.StartedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CompletedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.ValidUntilUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.DailyUpdateRun).WithMany(entity => entity.FetchCheckpoints).HasForeignKey(entity => entity.DailyUpdateRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Instrument).WithMany().HasForeignKey(entity => entity.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ReusedFromCheckpoint).WithMany().HasForeignKey(entity => entity.ReusedFromCheckpointId).OnDelete(DeleteBehavior.Restrict);
    }
}
