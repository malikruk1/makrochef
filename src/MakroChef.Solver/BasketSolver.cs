using Google.OrTools.Sat;
using MakroChef.Domain.Solver;

namespace MakroChef.Solver;

public class BasketSolver
{
    private const double TimeoutSeconds = 2.0;

    /// <summary>Cheap self-check that the native CP-SAT library loads on this host.</summary>
    public bool CanInitialize()
    {
        try
        {
            var model = new CpModel();
            model.NewIntVar(0, 1, "probe");
            var cpSolver = new CpSolver();
            cpSolver.StringParameters = "max_time_in_seconds:1";
            return true;
        }
        catch
        {
            return false;
        }
    }

    public SolverResult Solve(SolverRequest request)
    {
        var relaxed = new List<string>();

        var maxSugar = request.MaxSugarMg;
        var kcalMin = request.KcalMin;
        var kcalMax = request.KcalMax;
        var budget = request.BaselineCostKopecks;

        // Fixed relaxation order: 1) sugar ceiling, 2) kcal corridor, 3) budget +10%.
        for (var attempt = 0; attempt <= 3; attempt++)
        {
            var result = TrySolve(request, maxSugar, kcalMin, kcalMax, budget, relaxed);
            if (result is not null)
            {
                return result;
            }

            switch (attempt)
            {
                case 0:
                    maxSugar = (long)(request.MaxSugarMg * 1.5);
                    relaxed.Add($"sugar ceiling: {request.MaxSugarMg}mg -> {maxSugar}mg");
                    break;
                case 1:
                    kcalMin = (long)(request.KcalMin * 0.8);
                    kcalMax = (long)(request.KcalMax * 1.2);
                    relaxed.Add($"kcal corridor: [{request.KcalMin},{request.KcalMax}] -> [{kcalMin},{kcalMax}]");
                    break;
                case 2:
                    budget = (long)(request.BaselineCostKopecks * 1.1);
                    relaxed.Add($"budget: {request.BaselineCostKopecks} -> {budget} (+10%)");
                    break;
            }
        }

        return SolverResult.Failure(relaxed);
    }

    private static SolverResult? TrySolve(
        SolverRequest request,
        long maxSugar,
        long kcalMin,
        long kcalMax,
        long budget,
        IReadOnlyList<string> relaxed)
    {
        var model = new CpModel();
        var candidates = request.Candidates;
        var vars = new IntVar[candidates.Count];

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var upperBound = c.Restricted ? 0 : c.MaxUnits;
            vars[i] = model.NewIntVar(0, upperBound, $"x{i}");
        }

        model.Add(LinearExpr.WeightedSum(vars, candidates.Select(c => c.ProteinMg).ToArray()) >= request.TargetProteinMg);
        model.Add(LinearExpr.WeightedSum(vars, candidates.Select(c => c.SugarMg).ToArray()) <= maxSugar);

        var kcalSum = LinearExpr.WeightedSum(vars, candidates.Select(c => c.Kcal).ToArray());
        model.Add(kcalSum >= kcalMin);
        model.Add(kcalSum <= kcalMax);

        model.Add(LinearExpr.WeightedSum(vars, candidates.Select(c => c.PriceKopecks).ToArray()) <= budget);

        foreach (var categoryGroup in candidates.Select((c, i) => (c, i)).GroupBy(t => t.c.Category))
        {
            var categoryVars = categoryGroup.Select(t => vars[t.i]).ToArray();
            model.Add(LinearExpr.Sum(categoryVars) <= request.MaxUnitsPerCategory);
        }

        model.Minimize(LinearExpr.WeightedSum(vars, candidates.Select(c => c.PriceKopecks).ToArray()));

        var cpSolver = new CpSolver { StringParameters = $"max_time_in_seconds:{TimeoutSeconds}" };
        var status = cpSolver.Solve(model);

        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
        {
            return null;
        }

        var lines = new List<BasketLine>();
        long totalCost = 0, totalProtein = 0, totalSugar = 0, totalKcal = 0;

        for (var i = 0; i < candidates.Count; i++)
        {
            var units = (int)cpSolver.Value(vars[i]);
            if (units <= 0)
            {
                continue;
            }

            var c = candidates[i];
            lines.Add(new BasketLine(c.ProductId, units));
            totalCost += c.PriceKopecks * units;
            totalProtein += c.ProteinMg * units;
            totalSugar += c.SugarMg * units;
            totalKcal += c.Kcal * units;
        }

        return new SolverResult(true, lines, totalCost, totalProtein, totalSugar, totalKcal, relaxed);
    }
}
