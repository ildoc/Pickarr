namespace Pickarr.Core.Domain.Entities;

public sealed class ImdbRatingSnapshot
{
    public const int SingletonId = 1;

    public int Id { get; set; }
    public double MeanVote { get; set; }
    public double PriorMean { get; set; }
    public double TmdbOffset { get; set; }
    public int RowCount { get; set; }
    public DateTime ImportedAt { get; set; }
    public required string SourceUrl { get; set; }
}
