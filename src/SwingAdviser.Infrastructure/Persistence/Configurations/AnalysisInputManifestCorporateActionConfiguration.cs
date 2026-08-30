using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class AnalysisInputManifestCorporateActionConfiguration : IEntityTypeConfiguration<AnalysisInputManifestCorporateAction>
{
    public void Configure(EntityTypeBuilder<AnalysisInputManifestCorporateAction> builder)
    {
        builder.ToTable("analysis_input_manifest_corporate_actions");
        builder.HasKey(entity => new { entity.ManifestId, entity.CorporateActionId });

        builder.HasOne(entity => entity.Manifest)
            .WithMany(entity => entity.CorporateActions)
            .HasForeignKey(entity => entity.ManifestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.CorporateAction)
            .WithMany()
            .HasForeignKey(entity => entity.CorporateActionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
