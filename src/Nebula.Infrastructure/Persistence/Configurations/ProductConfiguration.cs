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
        // Case-insensitive text sorting, closer to the mock's collator.
        builder.Property(p => p.Sku).UseCollation(Collations.NoCase);
        builder.Property(p => p.Name).UseCollation(Collations.NoCase);
        builder.Property(p => p.Description).UseCollation(Collations.NoCase);
        builder.Property(p => p.ImageUrl).UseCollation(Collations.NoCase);

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
