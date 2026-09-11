using MakroChef.Solver;
using Xunit;

namespace MakroChef.Tests;

public class SolverSmokeTests
{
    [Fact]
    public void CanInitialize_ReturnsTrue_WhenNativeLibraryLoads()
    {
        var solver = new BasketSolver();
        Assert.True(solver.CanInitialize());
    }
}
