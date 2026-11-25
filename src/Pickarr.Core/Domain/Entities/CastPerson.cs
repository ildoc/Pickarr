namespace Pickarr.Core.Domain.Entities;

public sealed class CastPerson
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? ProfilePath { get; set; }
    public string? ImdbId { get; set; }
    public DateTime? ImdbFetchedAt { get; set; }

    public ICollection<TitleCastMember> TitleCastMembers { get; set; } = new List<TitleCastMember>();
}
