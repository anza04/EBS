using Ebm.Domain.Clubs;
using Ebm.Domain.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebm.Persistence.Configurations;

internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("Countries");
        builder.Property(x => x.Code).HasMaxLength(Country.CodeLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(Country.NameMaxLength).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class ArenaConfiguration : IEntityTypeConfiguration<Arena>
{
    public void Configure(EntityTypeBuilder<Arena> builder)
    {
        builder.ToTable("Arenas", t => t.HasCheckConstraint("CK_Arenas_Capacity", "Capacity > 0"));
        builder.Property(x => x.Name).HasMaxLength(Arena.NameMaxLength).IsRequired();
        builder.Property(x => x.City).HasMaxLength(Arena.CityMaxLength).IsRequired();
        builder.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId);
    }
}

internal sealed class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        builder.ToTable("Clubs");
        builder.Property(x => x.Name).HasMaxLength(Club.NameMaxLength).IsRequired();
        builder.Property(x => x.ShortName).HasMaxLength(Club.ShortNameMaxLength).IsRequired();
        builder.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId);
        builder.HasOne<Arena>().WithMany().HasForeignKey(x => x.HomeArenaId);
    }
}
