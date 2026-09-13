namespace MakroChef.Agent.Cart;

/// <summary>Output of <see cref="WeekOverWeekAnalyzer"/> — TASKS.md screen 6. Always retrospective
/// (built purely from past receipts, there is no live in-progress week to compare against), and
/// honestly reports when there isn't enough order history to compare two weeks rather than
/// inventing a trend.</summary>
public record WeekOverWeekResult(
    bool HasEnoughData,
    decimal LastWeekProteinGapGrams,
    decimal ThisWeekProteinGapGrams);
