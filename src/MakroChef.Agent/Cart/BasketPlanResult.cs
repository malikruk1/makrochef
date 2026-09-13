using MakroChef.Agent.Coverage;
using MakroChef.Domain.Profile;
using MakroChef.Domain.Solver;

namespace MakroChef.Agent.Cart;

/// <summary>Output of <see cref="BasketPlanner"/> — everything TASKS.md 6/screen-3 needs to show
/// the guest their optimized weekly basket against what they usually spend. Read-only: no cart
/// mutation happens here (TASKS.md 7.1 requires an explicit, separate confirmed action for
/// that).</summary>
public record BasketPlanResult(
    TargetNorms Norms,
    CoverageReport Coverage,
    int CandidatePoolSize,
    long BaselineWeeklyCostKopecks,
    SolverResult Solver);
