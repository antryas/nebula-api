using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nebula.Domain;

namespace Nebula.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => o.Number).IsUnique();
        builder.HasIndex(o => o.CreatedAt);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.CustomerId);
        // Case-insensitive ordering, like the mock's collator.
        builder.Property(o => o.CustomerName).UseCollation(Collations.NoCase);
        builder.Property(o => o.CustomerEmail).UseCollation(Collations.NoCase);

        builder.OwnsOne(o => o.ShippingAddress);

        builder.OwnsMany(o => o.Items, item =>
        {
            item.ToTable("OrderItems");
            item.Property<int>(OwnedKeys.RowId);
            item.HasKey(OwnedKeys.RowId);
        });

        builder.OwnsMany(o => o.History, change =>
        {
            change.ToTable("OrderStatusHistory");
            // Auto-increment row key preserves insertion (chronological) order.
            change.Property<int>(OwnedKeys.RowId);
            change.HasKey(OwnedKeys.RowId);
        });
    }
}
