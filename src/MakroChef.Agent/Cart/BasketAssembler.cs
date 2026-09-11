using MakroChef.Domain.Cart;
using MakroChef.Mcp;

namespace MakroChef.Agent.Cart;

/// <summary>TASKS.md 7.1. 🔒 needs a live cart session (BLOCKERS.md B-2).</summary>
public class BasketAssembler(IMakroChefMcpClient mcpClient, string companyId, string branchId)
{
    public async Task<CartState> GetCartAsync(CancellationToken cancellationToken = default)
    {
        var json = await mcpClient.CallToolAsync("get_shopping_cart_by_id", new Dictionary<string, object?>(), cancellationToken);
        return CartResponseParser.Parse(json);
    }

    /// <param name="confirmClearIfNotEmpty">Called only when the cart already has items —
    /// TASKS.md 7.1 requires asking the guest first, never clearing silently.</param>
    public async Task<CartState> AssembleAsync(
        IReadOnlyList<Domain.Solver.BasketLine> lines,
        Func<Task<bool>> confirmClearIfNotEmpty,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetCartAsync(cancellationToken);
        if (existing.Lines.Count > 0)
        {
            var shouldClear = await confirmClearIfNotEmpty();
            if (shouldClear)
            {
                await mcpClient.CallToolAsync("clear_shopping_cart", new Dictionary<string, object?>(), cancellationToken);
            }
        }

        await AddOrUpdateAsync(lines, cancellationToken);

        // TASKS.md 7.1: "Не довіряти success: true — перечитувати кошик."
        return await GetCartAsync(cancellationToken);
    }

    public Task AddOrUpdateAsync(IReadOnlyList<Domain.Solver.BasketLine> lines, CancellationToken cancellationToken = default) =>
        mcpClient.CallToolAsync(
            "add_or_update_cart_products",
            new Dictionary<string, object?>
            {
                ["items"] = lines.Select(l => new Dictionary<string, object?>
                {
                    ["productId"] = l.ProductId,
                    ["quantity"] = l.Units,
                    ["companyId"] = companyId,
                    ["branchId"] = branchId,
                }).ToList(),
            },
            cancellationToken);

    public Task RemoveAsync(IReadOnlyList<string> productIds, CancellationToken cancellationToken = default) =>
        productIds.Count == 0
            ? Task.CompletedTask
            : mcpClient.CallToolAsync(
                "remove_cart_products",
                new Dictionary<string, object?> { ["productIds"] = productIds },
                cancellationToken);
}
