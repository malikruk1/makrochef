using MakroChef.Agent.Coverage;
using MakroChef.Domain.Cart;
using MakroChef.Domain.Profile;
using MakroChef.Domain.Solver;

namespace MakroChef.Agent.Cart;

/// <summary>Output of <see cref="BasketPlanner"/> — everything TASKS.md 6/screen-3 needs to show
/// the guest their optimized weekly basket against what they usually spend. Read-only: no cart
/// mutation happens here (TASKS.md 7.1 requires an explicit, separate confirmed action for
/// that). <see cref="Session"/> is exposed so that separate, explicitly-confirmed action (POST
/// /api/basket/apply) can reuse the same bootstrapped session instead of re-resolving it.</summary>
public record BasketPlanResult(
    SessionContext Session,
    TargetNorms Norms,
    CoverageReport Coverage,
    int CandidatePoolSize,
    long BaselineWeeklyCostKopecks,
    SolverRequest Request,
    SolverResult Solver);
