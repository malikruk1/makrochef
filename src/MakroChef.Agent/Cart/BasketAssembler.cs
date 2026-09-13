using MakroChef.Domain.Cart;
using MakroChef.Mcp;

namespace MakroChef.Agent.Cart;

/// <summary>TASKS.md 7.1. 🔒 needs a live cart session (BLOCKERS.md B-2).
///
/// Confirmed live (2026-09-14): silpo_get_shopping_cart_by_id requires a shoppingCartId
/// parameter (SessionBootstrap already discovered this) — the previous version of this class
/// called it with no arguments at all and would fail validation on any real cart. Cart mutation
/// tools are scoped the same way as every other catalog tool, so shoppingCartId is threaded
/// through add/remove/clear too; companyId is per TASKS.md 3.1 baked into the server and never
/// passed.</summary>
public class BasketAssembler(IMakroChefMcpClient mcpClient, SessionContext session)
{
    public async Task<CartState> GetCartAsync(CancellationToken cancellationToken = default)
    {
        var json = await mcpClient.CallToolAsync(
            "silpo_get_shopping_cart_by_id",
            new Dictionary<string, object?> { ["shoppingCartId"] = session.ShoppingCartId },
            cancellationToken);
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
                await mcpClient.CallToolAsync(
                    "silpo_clear_shopping_cart",
                    new Dictionary<string, object?> { ["shoppingCartId"] = session.ShoppingCartId },
                    cancellationToken);
            }
        }

        await AddOrUpdateAsync(lines, cancellationToken);

        // TASKS.md 7.1: "Не довіряти success: true — перечитувати кошик."
        return await GetCartAsync(cancellationToken);
    }

    public Task AddOrUpdateAsync(IReadOnlyList<Domain.Solver.BasketLine> lines, CancellationToken cancellationToken = default) =>
        mcpClient.CallToolAsync(
            "silpo_add_or_update_cart_products",
            new Dictionary<string, object?>
            {
                ["shoppingCartId"] = session.ShoppingCartId,
                ["items"] = lines.Select(l => new Dictionary<string, object?>
                {
                    ["productId"] = l.ProductId,
                    ["quantity"] = l.Units,
                    ["branchId"] = session.BranchId,
                }).ToList(),
            },
            cancellationToken);

    public Task RemoveAsync(IReadOnlyList<string> productIds, CancellationToken cancellationToken = default) =>
        productIds.Count == 0
            ? Task.CompletedTask
            : mcpClient.CallToolAsync(
                "silpo_remove_cart_products",
                new Dictionary<string, object?>
                {
                    ["shoppingCartId"] = session.ShoppingCartId,
                    ["productIds"] = productIds,
                },
                cancellationToken);
}
