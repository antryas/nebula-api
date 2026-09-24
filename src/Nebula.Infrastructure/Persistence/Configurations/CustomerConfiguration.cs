using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nebula.Domain;

namespace Nebula.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        // Case-insensitive ordering, like the mock's collator.
        builder.Property(c => c.Name).UseCollation(Collations.NoCase);
        builder.Property(c => c.Email).UseCollation(Collations.NoCase);
        builder.Property(c => c.Country).UseCollation(Collations.NoCase);
        builder.Property(c => c.Notes).UseCollation(Collations.NoCase);
    }
}
