namespace Pickarr.Core.Domain.Entities;

public sealed class ImdbTitleRating
{
    public required string Tconst { get; set; }
    public double AverageRating { get; set; }
    public int NumVotes { get; set; }
}
