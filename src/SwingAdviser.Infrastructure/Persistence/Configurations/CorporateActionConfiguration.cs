using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class CorporateActionConfiguration : IEntityTypeConfiguration<CorporateAction>
{
    public void Configure(EntityTypeBuilder<CorporateAction> builder)
    {
        builder.ToTable("corporate_actions");
        builder.HasKey(entity => entity.CorporateActionId);
        builder.HasIndex(entity => new { entity.InstrumentId, entity.SourceEventId, entity.Revision }).IsUnique();

        builder.Property(entity => entity.EffectiveDate)
            .HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AnnouncedAtUtc)
            .HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AvailableAtUtc)
            .HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.FirstObservedAtUtc)
            .HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.DividendAmountPerShare)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.RecordedAtUtc)
            .HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.Instrument)
            .WithMany(entity => entity.CorporateActions)
            .HasForeignKey(entity => entity.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Supersedes)
            .WithMany()
            .HasForeignKey(entity => entity.SupersedesId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
