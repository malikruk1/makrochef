using MakroChef.Domain.Entities;
using MakroChef.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;

namespace MakroChef.Data;

/// <summary>Caches nutrient lookups in the (previously unused) ProductNutritions table, keyed by
/// whatever the inner resolver's own first argument is (a slug for ExactMcpNutritionResolver).
/// Confirmed live (2026-09-14): resolving ~178 real candidates cost one MCP round trip each just
/// for nutrients - on top of the get_product_details call CandidatePoolBuilder already makes for
/// the same product - and BasketPlanner rebuilds the whole candidate pool from scratch on every
/// single /api/basket, /api/basket/apply and /api/basket/reoptimize call in one demo session. A
/// short TTL cache turns every call after the first cold one into a DB read instead of a network
/// round trip for products already seen.</summary>
public class CachingNutritionResolver(INutritionResolver inner, MakroChefDbContext db) : INutritionResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    // EF Core's DbContext isn't thread-safe - CandidatePoolBuilder resolves candidates
    // concurrently now, and this resolver is shared across all of them within one request.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public async Task<NutrientInfo?> ResolveAsync(string productId, string? barcode, CancellationToken cancellationToken = default)
    {
        var cached = await ReadCacheAsync(productId, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // Deliberately unlocked: this is the slow MCP round trip, and CandidatePoolBuilder now
        // resolves candidates concurrently - only the DB access around it needs to serialize.
        var resolved = await inner.ResolveAsync(productId, barcode, cancellationToken);
        if (resolved is not null)
        {
            await UpsertAsync(productId, resolved, cancellationToken);
        }

        return resolved;
    }

    private async Task<NutrientInfo?> ReadCacheAsync(string productId, CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            var cutoff = DateTimeOffset.UtcNow - CacheTtl;
            var cached = await db.ProductNutritions
                .Where(p => p.ProductId == productId && p.FetchedAt > cutoff)
                .FirstOrDefaultAsync(cancellationToken);

            return cached is null
                ? null
                : new NutrientInfo(cached.ProteinPer100g, cached.FatPer100g, CarbsPer100g: null, cached.SugarPer100g, cached.KcalPer100g, cached.Source);
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task UpsertAsync(string productId, NutrientInfo nutrients, CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            var existing = await db.ProductNutritions.FirstOrDefaultAsync(p => p.ProductId == productId, cancellationToken);
            if (existing is not null)
            {
                existing.ProteinPer100g = nutrients.ProteinPer100g;
                existing.FatPer100g = nutrients.FatPer100g;
                existing.SugarPer100g = nutrients.SugarPer100g;
                existing.KcalPer100g = nutrients.KcalPer100g;
                existing.Source = nutrients.Source;
                existing.FetchedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.ProductNutritions.Add(new ProductNutrition
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    Name = productId, // no human name available at this layer - CandidatePoolBuilder has it, not this resolver
                    ProteinPer100g = nutrients.ProteinPer100g,
                    FatPer100g = nutrients.FatPer100g,
                    SugarPer100g = nutrients.SugarPer100g,
                    KcalPer100g = nutrients.KcalPer100g,
                    Source = nutrients.Source,
                    FetchedAt = DateTimeOffset.UtcNow,
                });
            }

            // Best-effort: a cache write failing (e.g. a concurrent insert racing on the unique
            // index) must never fail the actual nutrient lookup that already succeeded.
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
            }
        }
        finally
        {
            Lock.Release();
        }
    }
}
