using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nebula.Domain;

namespace Nebula.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.Category);

        builder.OwnsMany(p => p.Variants, variant =>
        {
            variant.ToTable("ProductVariants");
            // Surrogate row key keeps insertion order; the domain variant id is a plain column.
            variant.Property<int>(OwnedKeys.RowId);
            variant.HasKey(OwnedKeys.RowId);
            variant.Property(v => v.Id).HasColumnName("VariantId");
        });
    }
}
