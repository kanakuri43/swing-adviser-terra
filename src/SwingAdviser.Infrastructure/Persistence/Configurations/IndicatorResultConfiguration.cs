using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class IndicatorResultConfiguration : IEntityTypeConfiguration<IndicatorResult>
{
    public void Configure(EntityTypeBuilder<IndicatorResult> builder)
    {
        builder.ToTable("indicator_results");
        builder.HasKey(entity => entity.IndicatorResultId);
        builder.HasIndex(entity => new { entity.ScanRunId, entity.ManifestId, entity.StrategyParameterSnapshotId }).IsUnique();

        builder.Property(entity => entity.EvaluationBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AnalyzedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.MacdLine).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.MacdSignal).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.MacdHistogram).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Ema20).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Ema50).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Ema200).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Atr14).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.VolumeRatio).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.VolumeReferenceAverage).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CreatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.ScanRun).WithMany(entity => entity.IndicatorResults).HasForeignKey(entity => entity.ScanRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Instrument).WithMany().HasForeignKey(entity => entity.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Manifest).WithMany(entity => entity.IndicatorResults).HasForeignKey(entity => entity.ManifestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.StrategyParameterSnapshot).WithMany(entity => entity.IndicatorResults).HasForeignKey(entity => entity.StrategyParameterSnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}
