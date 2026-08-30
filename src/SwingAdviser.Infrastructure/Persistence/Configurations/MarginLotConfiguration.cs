using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class MarginLotConfiguration : IEntityTypeConfiguration<MarginLot>
{
    public void Configure(EntityTypeBuilder<MarginLot> builder)
    {
        builder.ToTable("margin_lots");
        builder.HasKey(entity => entity.MarginLotId);
        builder.HasIndex(entity => entity.OpeningTradeExecutionId).IsUnique();
        builder.Property(entity => entity.CurrentQuantity).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.Position).WithMany(entity => entity.MarginLots).HasForeignKey(entity => entity.PositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.OpeningTradeExecution).WithMany().HasForeignKey(entity => entity.OpeningTradeExecutionId).OnDelete(DeleteBehavior.Restrict);
    }
}
