using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickarr.Core.Domain.Entities;

namespace Pickarr.Infrastructure.Persistence.Configurations;

public sealed class CastPersonConfiguration : IEntityTypeConfiguration<CastPerson>
{
    public void Configure(EntityTypeBuilder<CastPerson> builder)
    {
        builder.ToTable("CastPeople");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ProfilePath).HasMaxLength(256);
        builder.Property(x => x.ImdbId).HasMaxLength(32);
    }
}

public sealed class TitleCastMemberConfiguration : IEntityTypeConfiguration<TitleCastMember>
{
    public void Configure(EntityTypeBuilder<TitleCastMember> builder)
    {
        builder.ToTable("TitleCastMembers");
        builder.HasKey(x => new { x.TitleId, x.PersonId });
        builder.HasIndex(x => new { x.TitleId, x.Position }).IsUnique();
        builder.HasOne(x => x.Title)
            .WithMany(x => x.CastMembers)
            .HasForeignKey(x => x.TitleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Person)
            .WithMany(x => x.TitleCastMembers)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
