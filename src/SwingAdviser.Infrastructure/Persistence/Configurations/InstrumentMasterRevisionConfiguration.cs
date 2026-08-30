using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class InstrumentMasterRevisionConfiguration : IEntityTypeConfiguration<InstrumentMasterRevision>
{
    public void Configure(EntityTypeBuilder<InstrumentMasterRevision> builder)
    {
        builder.ToTable("instrument_master_revisions");
        builder.HasKey(entity => entity.InstrumentMasterRevisionId);
        builder.HasIndex(entity => new { entity.InstrumentId, entity.Revision }).IsUnique();
        builder.HasIndex(entity => entity.SupersedesRevisionId).IsUnique().HasFilter("supersedes_revision_id IS NOT NULL");

        builder.Property(entity => entity.EffectiveAtDate)
            .HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AvailableAtUtc)
            .HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.RecordedAtUtc)
            .HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.Instrument)
            .WithMany(entity => entity.MasterRevisions)
            .HasForeignKey(entity => entity.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.SupersedesRevision)
            .WithMany()
            .HasForeignKey(entity => entity.SupersedesRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
