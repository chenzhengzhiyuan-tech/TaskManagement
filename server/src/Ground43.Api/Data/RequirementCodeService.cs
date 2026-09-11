using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Data;

public sealed class RequirementCodeService(AppDbContext db)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _next;

    public async Task<string> NextAsync(CancellationToken cancellationToken) => (await NextRangeAsync(1, cancellationToken))[0];

    public async Task<IReadOnlyList<string>> NextRangeAsync(int count, CancellationToken cancellationToken)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var ids = await db.Requirements.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
            var max = ids.Select(id => int.TryParse(id.Replace("REQ-", "", StringComparison.OrdinalIgnoreCase), out var value) ? value : 0).DefaultIfEmpty().Max();
            _next = Math.Max(_next, max + 1);
            var result = Enumerable.Range(_next, count).Select(value => $"REQ-{value:D4}").ToArray();
            _next += count;
            return result;
        }
        finally { Gate.Release(); }
    }
}
