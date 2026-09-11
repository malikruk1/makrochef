using MakroChef.Domain.Nutrition;
using MakroChef.Mcp;

namespace MakroChef.Nutrition;

public enum NutritionResolverMode
{
    /// <summary>Coverage probe (3.4) came back >= 60%.</summary>
    Exact,

    /// <summary>Coverage probe (3.4) came back &lt; 60%.</summary>
    CategoryIndex,
}

/// <summary>The one line TASKS.md 4.0 says should need to change once the coverage decision
/// (BLOCKERS.md B-5) is made — everything else about the two resolvers is already written.</summary>
public static class NutritionResolverFactory
{
    public static INutritionResolver Create(NutritionResolverMode mode, IMakroChefMcpClient mcpClient)
    {
        var exact = new ExactMcpNutritionResolver(mcpClient);
        return mode switch
        {
            NutritionResolverMode.Exact => exact,
            NutritionResolverMode.CategoryIndex => new CategoryIndexNutritionResolver(exact, new OpenFoodFactsClient()),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }
}
