namespace Pickarr.Core.Domain.Entities;

public sealed class TitleCastMember
{
    public int TitleId { get; set; }
    public int PersonId { get; set; }
    public int Position { get; set; }

    public Title? Title { get; set; }
    public CastPerson? Person { get; set; }
}
