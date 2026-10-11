using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Services;

/// <summary>
/// Pure account-hierarchy rollup for financial statements. A group account that carries a
/// statement classification reports the signed balance of every posted leaf account of that same
/// class beneath it, at any depth. Groups without a classification are not rolled up, and leaves of
/// another class never contribute to a group. Broken hierarchies (missing parents or cycles) fail
/// loudly instead of producing subtotals that look trustworthy.
/// </summary>
public static class StatementRollupCalculator
{
    /// <summary>Computes one rollup row per classified group account for the requested classes.</summary>
    public static IReadOnlyList<StatementRollupLine> Build(
        IReadOnlyCollection<StatementAccountNode> accounts,
        IReadOnlyCollection<StatementPostingRow> postings,
        IReadOnlyCollection<AccountClass> classes)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(postings);
        ArgumentNullException.ThrowIfNull(classes);

        var byId = new Dictionary<int, StatementAccountNode>(accounts.Count);
        foreach (var account in accounts)
        {
            if (!byId.TryAdd(account.ChartOfAccountId, account))
                throw new InvalidOperationException(
                    $"Chart account {account.ChartOfAccountId} appears more than once in the statement hierarchy.");
        }

        var requested = classes.ToHashSet();
        var totals = new Dictionary<(int GroupId, AccountClass Class), decimal>();
        foreach (var posting in postings)
        {
            if (!requested.Contains(posting.AccountClass))
                continue;
            if (!byId.TryGetValue(posting.ChartOfAccountId, out var leaf))
                throw new InvalidOperationException(
                    $"Posted chart account {posting.ChartOfAccountId} is missing from the statement hierarchy.");

            foreach (var ancestor in Ancestors(leaf, byId))
            {
                if (!ancestor.IsGroupAccount || ancestor.AccountClass != posting.AccountClass)
                    continue;
                totals.TryGetValue((ancestor.ChartOfAccountId, posting.AccountClass), out var current);
                totals[(ancestor.ChartOfAccountId, posting.AccountClass)] = current + posting.Amount;
            }
        }

        return byId.Values
            .Where(account => account.IsGroupAccount &&
                              account.AccountClass is { } accountClass &&
                              requested.Contains(accountClass))
            .OrderBy(account => account.AccountClass)
            .ThenBy(account => account.AccountCode, StringComparer.Ordinal)
            .ThenBy(account => account.ChartOfAccountId)
            .Select(account => new StatementRollupLine(
                account.ChartOfAccountId,
                account.AccountCode,
                account.AccountName,
                account.AccountClass!.Value,
                account.ParentChartOfAccountId,
                Depth(account, byId),
                totals.TryGetValue((account.ChartOfAccountId, account.AccountClass!.Value), out var amount)
                    ? amount
                    : 0m))
            .ToList();
    }

    private static IEnumerable<StatementAccountNode> Ancestors(
        StatementAccountNode start,
        IReadOnlyDictionary<int, StatementAccountNode> byId)
    {
        var visited = new HashSet<int> { start.ChartOfAccountId };
        var parentId = start.ParentChartOfAccountId;
        while (parentId is int id)
        {
            var parent = Resolve(id, byId);
            if (!visited.Add(parent.ChartOfAccountId))
                throw new InvalidOperationException(
                    $"Chart account hierarchy contains a cycle at account {parent.AccountCode}.");
            yield return parent;
            parentId = parent.ParentChartOfAccountId;
        }
    }

    private static int Depth(StatementAccountNode account, IReadOnlyDictionary<int, StatementAccountNode> byId) =>
        Ancestors(account, byId).Count();

    private static StatementAccountNode Resolve(int id, IReadOnlyDictionary<int, StatementAccountNode> byId) =>
        byId.TryGetValue(id, out var node)
            ? node
            : throw new InvalidOperationException($"Chart account parent {id} is missing from the statement hierarchy.");
}
