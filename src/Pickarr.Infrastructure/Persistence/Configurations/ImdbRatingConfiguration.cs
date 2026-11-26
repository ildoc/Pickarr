using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickarr.Core.Domain.Entities;

namespace Pickarr.Infrastructure.Persistence.Configurations;

public sealed class ImdbTitleRatingConfiguration : IEntityTypeConfiguration<ImdbTitleRating>
{
    public void Configure(EntityTypeBuilder<ImdbTitleRating> builder)
    {
        builder.ToTable("ImdbTitleRatings");
        builder.HasKey(x => x.Tconst);
        builder.Property(x => x.Tconst).HasMaxLength(16);
        builder.Property(x => x.AverageRating).IsRequired();
        builder.Property(x => x.NumVotes).IsRequired();
    }
}

public sealed class ImdbRatingSnapshotConfiguration : IEntityTypeConfiguration<ImdbRatingSnapshot>
{
    public void Configure(EntityTypeBuilder<ImdbRatingSnapshot> builder)
    {
        builder.ToTable("ImdbRatingSnapshot");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SourceUrl).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ImportedAt).IsRequired();
    }
}
