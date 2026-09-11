namespace MakroChef.Agent.Coverage;

public record CoverageReport(
    int UniqueSkuCount,
    int FullMacroCount,
    IReadOnlyDictionary<string, int> CountByCategory,
    IReadOnlyList<string> Gaps,
    decimal? MedianWeeklyReceipt)
{
    public double CoveragePercent => UniqueSkuCount == 0 ? 0 : 100.0 * FullMacroCount / UniqueSkuCount;

    public string ToMarkdown()
    {
        var categoryLines = CountByCategory.Count == 0
            ? "немає даних"
            : string.Join(", ", CountByCategory.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));

        var gapsLine = Gaps.Count == 0 ? "не виявлено" : string.Join(", ", Gaps);
        var medianLine = MedianWeeklyReceipt is null ? "немає даних" : $"{MedianWeeklyReceipt.Value:F2} ₴";

        return $"""
            Покриття: {CoveragePercent:F0}%
            Унікальних SKU: {UniqueSkuCount}
            З повним Б/Ж/В/цукром: {FullMacroCount}
            Розріз по категоріях: {categoryLines}
            Дірки: {gapsLine}
            Медіанний тижневий чек: {medianLine}
            """;
    }
}
