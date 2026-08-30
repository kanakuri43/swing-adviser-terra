using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class AnalysisInputManifestConfiguration : IEntityTypeConfiguration<AnalysisInputManifest>
{
    public void Configure(EntityTypeBuilder<AnalysisInputManifest> builder)
    {
        builder.ToTable("analysis_input_manifests");
        builder.HasKey(entity => entity.ManifestId);
        builder.HasIndex(entity => new
        {
            entity.InstrumentId,
            entity.EvaluationBarDate,
            entity.AnalyzedAtUtc,
            entity.ManifestHash,
        }).IsUnique();

        builder.Property(entity => entity.EvaluationBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AnalyzedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.FirstBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.LastBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CreatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.Instrument)
            .WithMany()
            .HasForeignKey(entity => entity.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
