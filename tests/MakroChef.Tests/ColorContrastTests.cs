using System.Text.RegularExpressions;
using Xunit;

namespace MakroChef.Tests;

/// <summary>Gate 8.1: --text-muted and --text-compare must stay readable (WCAG AA, 4.5:1)
/// against --bg and --card, in both themes, without re-typing the hex values here —
/// this test reads them straight out of web/tokens/tokens.css.</summary>
public class ColorContrastTests
{
    private static readonly Dictionary<string, string> Light = ParseBlock(ReadTokensCss(), @":root\s*\{([^}]*)\}");
    private static readonly Dictionary<string, string> Dark = ParseBlock(ReadTokensCss(), @"\[data-theme=""dark""\]\s*\{([^}]*)\}");

    [Theory]
    [InlineData("text-muted", "bg")]
    [InlineData("text-muted", "card")]
    [InlineData("text-compare", "bg")]
    [InlineData("text-compare", "card")]
    public void LightTheme_TextOnSurface_MeetsWcagAa(string textVar, string surfaceVar)
    {
        AssertContrastAtLeast(Light[textVar], Light[surfaceVar], 4.5, $"light {textVar} on {surfaceVar}");
    }

    [Theory]
    [InlineData("text-muted", "bg")]
    [InlineData("text-muted", "card")]
    [InlineData("text-compare", "bg")]
    [InlineData("text-compare", "card")]
    public void DarkTheme_TextOnSurface_MeetsWcagAa(string textVar, string surfaceVar)
    {
        AssertContrastAtLeast(Dark[textVar], Dark[surfaceVar], 4.5, $"dark {textVar} on {surfaceVar}");
    }

    private static void AssertContrastAtLeast(string hexA, string hexB, double minRatio, string label)
    {
        var ratio = ContrastRatio(hexA, hexB);
        Assert.True(ratio >= minRatio, $"{label}: contrast {ratio:F2}:1 < {minRatio}:1 ({hexA} vs {hexB})");
    }

    private static double ContrastRatio(string hexA, string hexB)
    {
        var lumA = RelativeLuminance(hexA);
        var lumB = RelativeLuminance(hexB);
        var (lighter, darker) = lumA >= lumB ? (lumA, lumB) : (lumB, lumA);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToInt32(hex[..2], 16) / 255.0;
        var g = Convert.ToInt32(hex[2..4], 16) / 255.0;
        var b = Convert.ToInt32(hex[4..6], 16) / 255.0;

        double Channel(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static Dictionary<string, string> ParseBlock(string css, string blockPattern)
    {
        var block = Regex.Match(css, blockPattern, RegexOptions.Singleline).Groups[1].Value;
        var result = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(block, @"--([\w-]+):\s*(#[0-9A-Fa-f]{6})"))
        {
            result[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return result;
    }

    private static string ReadTokensCss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MakroChef.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new FileNotFoundException("Could not locate repo root (MakroChef.slnx) from test base directory.");
        }

        return File.ReadAllText(Path.Combine(dir.FullName, "web", "tokens", "tokens.css"));
    }
}
