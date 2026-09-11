using Google.OrTools.Sat;

namespace MakroChef.Solver;

public class BasketSolver
{
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
}
