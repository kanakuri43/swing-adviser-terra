using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class FundamentalDataSnapshotConfiguration : IEntityTypeConfiguration<FundamentalDataSnapshot>
{
    public void Configure(EntityTypeBuilder<FundamentalDataSnapshot> builder)
    {
        builder.ToTable("fundamental_data_snapshots");
        builder.HasKey(entity => entity.FundamentalSnapshotId);

        builder.Property(entity => entity.FetchedAtUtc)
            .HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Per)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Pbr)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.MarketCap)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.DividendYield)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.Instrument)
            .WithMany(entity => entity.FundamentalDataSnapshots)
            .HasForeignKey(entity => entity.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
