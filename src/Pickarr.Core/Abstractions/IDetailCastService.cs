namespace Pickarr.Core.Abstractions;

public sealed record DetailCastBatchResult(
    int Processed,
    int Updated,
    int Failed,
    int Empty,
    string? LastError = null);

public interface IDetailCastService
{
    Task<DetailCastBatchResult> ProcessBatchAsync(
        int limit = 50,
        int olderThanDays = 30,
        CancellationToken cancellationToken = default);
}
