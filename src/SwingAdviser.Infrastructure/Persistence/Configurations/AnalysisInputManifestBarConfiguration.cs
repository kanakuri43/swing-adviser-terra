using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class AnalysisInputManifestBarConfiguration : IEntityTypeConfiguration<AnalysisInputManifestBar>
{
    public void Configure(EntityTypeBuilder<AnalysisInputManifestBar> builder)
    {
        builder.ToTable("analysis_input_manifest_bars");
        builder.HasKey(entity => new { entity.ManifestId, entity.TradingDate });
        builder.Property(entity => entity.TradingDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.Manifest)
            .WithMany(entity => entity.Bars)
            .HasForeignKey(entity => entity.ManifestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.DailyBar)
            .WithMany()
            .HasForeignKey(entity => entity.DailyBarId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
