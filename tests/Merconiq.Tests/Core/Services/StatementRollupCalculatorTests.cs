using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Models;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>
/// Pure hierarchy-rollup rules for financial statements: same-class subtotals at every depth,
/// class isolation, classification requirements, and loud failure on broken hierarchies.
/// </summary>
public class StatementRollupCalculatorTests
{
    private static readonly AccountClass[] IncomeAndExpense = [AccountClass.Income, AccountClass.Expense];

    [Fact]
    public void Build_RollsSameClassPostingsIntoEveryAncestorGroupWithDepth()
    {
        var accounts = new[]
        {
            Group(1, "4000", "Revenue", parent: null, AccountClass.Income),
            Group(2, "4100", "Product Sales", parent: 1, AccountClass.Income),
            Leaf(3, "4110", "Domestic Sales", parent: 2, AccountClass.Income),
            Leaf(4, "4200", "Service Sales", parent: 1, AccountClass.Income),
            Group(5, "5000", "Expenses", parent: null, AccountClass.Expense),
            Leaf(6, "5100", "Cost of Sales", parent: 5, AccountClass.Expense)
        };
        var postings = new[]
        {
            new StatementPostingRow(3, AccountClass.Income, 100m),
            new StatementPostingRow(4, AccountClass.Income, 40m),
            new StatementPostingRow(6, AccountClass.Expense, 30m)
        };

        var rollups = StatementRollupCalculator.Build(accounts, postings, IncomeAndExpense);

        rollups.Should().HaveCount(3);
        rollups[0].Should().BeEquivalentTo(new StatementRollupLine(
            1, "4000", "Revenue", AccountClass.Income, null, 0, 140m));
        rollups[1].Should().BeEquivalentTo(new StatementRollupLine(
            2, "4100", "Product Sales", AccountClass.Income, 1, 1, 100m));
        rollups[2].Should().BeEquivalentTo(new StatementRollupLine(
            5, "5000", "Expenses", AccountClass.Expense, null, 0, 30m));
    }

    [Fact]
    public void Build_LeafOfAnotherClassNeverContributesToAGroup()
    {
        var accounts = new[]
        {
            Group(1, "1000", "Assets", parent: null, AccountClass.Income),
            Leaf(2, "1100", "Mislabelled", parent: 1, AccountClass.Expense)
        };
        var postings = new[] { new StatementPostingRow(2, AccountClass.Expense, 50m) };

        var rollups = StatementRollupCalculator.Build(accounts, postings, IncomeAndExpense);

        rollups.Should().ContainSingle()
            .Which.Amount.Should().Be(0m, "the group is Income; an Expense leaf must not count");
    }

    [Fact]
    public void Build_OmitsGroupsWithoutAClassification()
    {
        var accounts = new[]
        {
            Group(1, "9000", "Summary", parent: null, accountClass: null),
            Leaf(2, "9100", "Bank", parent: 1, AccountClass.Asset)
        };
        var postings = new[] { new StatementPostingRow(2, AccountClass.Asset, 10m) };

        var rollups = StatementRollupCalculator.Build(accounts, postings, [AccountClass.Asset]);

        rollups.Should().BeEmpty("unclassified groups are presentation-only and are not rolled up");
    }

    [Fact]
    public void Build_EmitsRequestedGroupsEvenWhenTheyHaveNoPostings()
    {
        var accounts = new[]
        {
            Group(1, "2000", "Liabilities", parent: null, AccountClass.Liability),
            Leaf(2, "2100", "Payables", parent: 1, AccountClass.Liability)
        };

        var rollups = StatementRollupCalculator.Build(accounts, [], [AccountClass.Liability]);

        rollups.Should().ContainSingle().Which.Amount.Should().Be(0m);
    }

    [Fact]
    public void Build_OnlyReturnsRequestedClasses()
    {
        var accounts = new[]
        {
            Group(1, "1000", "Assets", parent: null, AccountClass.Asset),
            Group(2, "4000", "Revenue", parent: null, AccountClass.Income)
        };

        var rollups = StatementRollupCalculator.Build(accounts, [], [AccountClass.Asset]);

        rollups.Select(line => line.AccountCode).Should().Equal("1000");
    }

    [Fact]
    public void Build_RejectsAHierarchyCycle()
    {
        var accounts = new[]
        {
            Group(1, "7000", "First", parent: 2, AccountClass.Income),
            Group(2, "7100", "Second", parent: 1, AccountClass.Income)
        };

        var act = () => StatementRollupCalculator.Build(accounts, [], [AccountClass.Income]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cycle*");
    }

    [Fact]
    public void Build_RejectsAParentMissingFromTheCompanyHierarchy()
    {
        var accounts = new[] { Group(1, "4000", "Revenue", parent: 99, AccountClass.Income) };

        var act = () => StatementRollupCalculator.Build(accounts, [], [AccountClass.Income]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*parent 99 is missing*");
    }

    [Fact]
    public void Build_RejectsAPostingOnAnAccountOutsideTheHierarchy()
    {
        var accounts = new[] { Group(1, "4000", "Revenue", parent: null, AccountClass.Income) };
        var postings = new[] { new StatementPostingRow(42, AccountClass.Income, 5m) };

        var act = () => StatementRollupCalculator.Build(accounts, postings, [AccountClass.Income]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*account 42 is missing*");
    }

    [Fact]
    public void Build_RejectsDuplicateAccountIdentifiers()
    {
        var accounts = new[]
        {
            Group(1, "4000", "Revenue", parent: null, AccountClass.Income),
            Group(1, "4001", "Duplicate", parent: null, AccountClass.Income)
        };

        var act = () => StatementRollupCalculator.Build(accounts, [], [AccountClass.Income]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than once*");
    }

    private static StatementAccountNode Group(
        int id,
        string code,
        string name,
        int? parent,
        AccountClass? accountClass) =>
        new(id, code, name, parent, IsGroupAccount: true, accountClass);

    private static StatementAccountNode Leaf(
        int id,
        string code,
        string name,
        int? parent,
        AccountClass accountClass) =>
        new(id, code, name, parent, IsGroupAccount: false, accountClass);
}
