using MakroChef.Agent.Cart;
using MakroChef.Agent.Profiling;
using MakroChef.Agent.Tracing;
using MakroChef.Api.Commands;
using MakroChef.Data;
using MakroChef.Domain.Cart;
using MakroChef.Domain.Profile;
using MakroChef.Mcp;
using MakroChef.Mcp.OAuth;
using MakroChef.Nutrition;
using MakroChef.Solver;
using Microsoft.EntityFrameworkCore;

if (args is ["auth"])
{
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var authConnectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
        ?? "Host=localhost;Database=makrochef;Username=postgres;Password=postgres";
    var authEncryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    using var authDb = new MakroChefDbContext(new DbContextOptionsBuilder<MakroChefDbContext>().UseNpgsql(authConnectionString).Options);
    var authTokenStore = new EfMcpTokenStore(authDb);
    var authTokenEncryptor = new TokenEncryptor(authEncryptionKey);

    return await AuthCommand.RunAsync(mcpBaseUri, devUserId, authTokenStore, authTokenEncryptor);
}

if (args is ["diag"])
{
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var diagConnectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
        ?? "Host=localhost;Database=makrochef;Username=postgres;Password=postgres";
    var diagEncryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    using var diagDb = new MakroChefDbContext(new DbContextOptionsBuilder<MakroChefDbContext>().UseNpgsql(diagConnectionString).Options);
    var diagTokenStore = new EfMcpTokenStore(diagDb);
    var diagTokenEncryptor = new TokenEncryptor(diagEncryptionKey);

    return await DiagCommand.RunAsync(mcpBaseUri, devUserId, diagDb, diagTokenStore, diagTokenEncryptor);
}

if (args is ["probe"])
{
    // No real user/session model yet (that's section 4) - a single dev user until then.
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var probeConnectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
        ?? "Host=localhost;Database=makrochef;Username=postgres;Password=postgres";
    var encryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    using var db = new MakroChefDbContext(new DbContextOptionsBuilder<MakroChefDbContext>().UseNpgsql(probeConnectionString).Options);
    var tokenStore = new EfMcpTokenStore(db);
    var tokenEncryptor = new TokenEncryptor(encryptionKey);

    return await ProbeCommand.RunAsync(mcpBaseUri, devUserId, db, tokenStore, tokenEncryptor);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=makrochef;Username=postgres;Password=postgres";

builder.Services.AddDbContext<MakroChefDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<BasketSolver>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var webDir = Path.Combine(MakroChef.Api.RepoPaths.FindRoot(), "web");
if (Directory.Exists(webDir))
{
    app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webDir),
        RequestPath = "/web",
    });
}

app.MapGet("/api/trace/{sessionId:guid}", async (Guid sessionId, MakroChefDbContext db) =>
    Results.Ok(await MakroChef.Api.TraceQuery.GetCallsAsync(db, sessionId)));

app.MapGet("/api/profile", async (MakroChefDbContext db) =>
{
    // No real user/session model yet (that's section 4 broader work) - a single dev user until then.
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var encryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    var tokenStore = new EfMcpTokenStore(db);
    var stored = await tokenStore.FindByUserAsync(devUserId);
    if (stored is null)
    {
        return Results.Problem("Немає збереженого MCP-токена. Виконайте: dotnet run -- auth", statusCode: 503);
    }

    var tokenEncryptor = new TokenEncryptor(encryptionKey);
    var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
    var recorder = new EfMcpCallRecorder(db);
    await using var mcpClient = new MakroChefMcpClient(mcpBaseUri, new FixedTokenProvider(accessToken), recorder, devUserId);

    GuestProfile profile;
    try
    {
        profile = await new GuestContextCollector(mcpClient).CollectAsync();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося прочитати профіль з MCP: {ex.Message}", statusCode: 502);
    }

    // Real per-receipt calorie derivation needs the slug/branchId product-detail pipeline
    // (BLOCKERS.md, 2026-09-14 entry) - not wired yet, so this stays honestly at the
    // "estimate" tier rather than faking a measured number.
    var norms = new TargetNormsCalculator().Compute(profile, medianDailyKcal: null);

    return Results.Ok(new
    {
        ageYears = profile.AgeYears,
        familySize = 1 + profile.Family.Count,
        restrictions = profile.Restrictions,
        hasSavedAddress = profile.HasSavedAddress,
        loyaltyBonus = profile.LoyaltyBonusBalance,
        targetProteinGrams = norms.ProteinTargetGrams,
        maxSugarGrams = norms.MaxSugarGrams,
        kcalMin = norms.KcalMin,
        kcalMax = norms.KcalMax,
        normSource = norms.Source,
    });
});

app.MapGet("/api/basket", async (MakroChefDbContext db, BasketSolver solver) =>
{
    // No real user/session model yet (that's section 4 broader work) - a single dev user until then.
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var encryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    var tokenStore = new EfMcpTokenStore(db);
    var stored = await tokenStore.FindByUserAsync(devUserId);
    if (stored is null)
    {
        return Results.Problem("Немає збереженого MCP-токена. Виконайте: dotnet run -- auth", statusCode: 503);
    }

    var tokenEncryptor = new TokenEncryptor(encryptionKey);
    var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
    var recorder = new EfMcpCallRecorder(db);
    await using var mcpClient = new MakroChefMcpClient(mcpBaseUri, new FixedTokenProvider(accessToken), recorder, devUserId);
    var loggingSolver = new LoggingBasketSolver(solver, recorder);

    BasketPlanResult? plan;
    try
    {
        plan = await new BasketPlanner(mcpClient, loggingSolver).PlanAsync();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося побудувати кошик з MCP: {ex.Message}", statusCode: 502);
    }

    if (plan is null)
    {
        return Results.Problem(
            "У гостя ще немає кошика — потрібно спершу обрати адресу/філію в застосунку Сільпо (створення кошика тут не реалізовано).",
            statusCode: 409);
    }

    if (!plan.Solver.Success)
    {
        return Results.Ok(new
        {
            success = false,
            relaxed = plan.Solver.Relaxed,
            candidatePoolSize = plan.CandidatePoolSize,
            coveragePercent = plan.Coverage.CoveragePercent,
        });
    }

    var nameByProductId = plan.Request.Candidates.ToDictionary(c => c.ProductId, c => c.Name);

    return Results.Ok(new
    {
        success = true,
        usualWeeklyTotal = plan.BaselineWeeklyCostKopecks / 100m,
        optimizedTotal = plan.Solver.TotalCostKopecks / 100m,
        totalProteinGrams = plan.Solver.TotalProteinMg / 1000m,
        totalSugarGrams = plan.Solver.TotalSugarMg / 1000m,
        totalKcal = plan.Solver.TotalKcal,
        lines = plan.Solver.Lines.Select(l => new { productId = l.ProductId, name = nameByProductId.GetValueOrDefault(l.ProductId), units = l.Units }),
        relaxed = plan.Solver.Relaxed,
        candidatePoolSize = plan.CandidatePoolSize,
        coveragePercent = plan.Coverage.CoveragePercent,
        targetProteinGrams = plan.Norms.ProteinTargetGrams,
        maxSugarGrams = plan.Norms.MaxSugarGrams,
        normSource = plan.Norms.Source,
    });
});

app.MapPost("/api/basket/apply", async (MakroChefDbContext db, BasketSolver solver, ApplyBasketRequest? body) =>
{
    var confirmClear = body?.ConfirmClear ?? false;

    // No real user/session model yet (that's section 4 broader work) - a single dev user until then.
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var encryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    var tokenStore = new EfMcpTokenStore(db);
    var stored = await tokenStore.FindByUserAsync(devUserId);
    if (stored is null)
    {
        return Results.Problem("Немає збереженого MCP-токена. Виконайте: dotnet run -- auth", statusCode: 503);
    }

    var tokenEncryptor = new TokenEncryptor(encryptionKey);
    var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
    var recorder = new EfMcpCallRecorder(db);
    await using var mcpClient = new MakroChefMcpClient(mcpBaseUri, new FixedTokenProvider(accessToken), recorder, devUserId);
    var loggingSolver = new LoggingBasketSolver(solver, recorder);

    BasketPlanResult? plan;
    try
    {
        plan = await new BasketPlanner(mcpClient, loggingSolver).PlanAsync();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося побудувати кошик з MCP: {ex.Message}", statusCode: 502);
    }

    if (plan is null)
    {
        return Results.Problem(
            "У гостя ще немає кошика — потрібно спершу обрати адресу/філію в застосунку Сільпо.",
            statusCode: 409);
    }

    if (!plan.Solver.Success)
    {
        return Results.Problem("Солвер не знайшов рішення — немає що застосовувати. Дивись /api/basket.", statusCode: 409);
    }

    var assembler = new BasketAssembler(mcpClient, plan.Session);

    CartState existing;
    try
    {
        existing = await assembler.GetCartAsync();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося прочитати поточний кошик: {ex.Message}", statusCode: 502);
    }

    // TASKS.md 7.1: "питати гостя, якщо кошик не порожній" - never clear silently. The Mini App
    // must show this count to the guest and let them explicitly confirm before we retry with
    // confirmClear: true.
    if (existing.Lines.Count > 0 && !confirmClear)
    {
        return Results.Json(new
        {
            needsConfirmation = true,
            existingLineCount = existing.Lines.Count,
            message = $"У кошику вже є {existing.Lines.Count} позицій. Повторіть запит із confirmClear:true, щоб очистити й замінити їх.",
        }, statusCode: 409);
    }

    CartState finalCart;
    try
    {
        finalCart = await assembler.AssembleAsync(
            plan.Solver.Lines,
            confirmClearIfNotEmpty: () => Task.FromResult(confirmClear));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося оновити кошик у Сільпо: {ex.Message}", statusCode: 502);
    }

    return Results.Ok(new
    {
        success = true,
        lines = finalCart.Lines.Select(l => new { productId = l.ProductId, name = l.Name, quantity = l.Quantity }),
        totalAfterDiscounts = finalCart.TotalKopecks / 100m,
        validations = finalCart.Validations.Select(v => new { productId = v.ProductId, reason = v.Reason, isOutOfStock = v.IsOutOfStock }),
    });
});

app.MapPost("/api/basket/reoptimize", async (MakroChefDbContext db, BasketSolver solver) =>
{
    // No real user/session model yet (that's section 4 broader work) - a single dev user until then.
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var encryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    var tokenStore = new EfMcpTokenStore(db);
    var stored = await tokenStore.FindByUserAsync(devUserId);
    if (stored is null)
    {
        return Results.Problem("Немає збереженого MCP-токена. Виконайте: dotnet run -- auth", statusCode: 503);
    }

    var tokenEncryptor = new TokenEncryptor(encryptionKey);
    var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
    var recorder = new EfMcpCallRecorder(db);
    await using var mcpClient = new MakroChefMcpClient(mcpBaseUri, new FixedTokenProvider(accessToken), recorder, devUserId);
    var loggingSolver = new LoggingBasketSolver(solver, recorder);

    BasketPlanResult? plan;
    try
    {
        plan = await new BasketPlanner(mcpClient, loggingSolver).PlanAsync();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося перерахувати план для переоптимізації: {ex.Message}", statusCode: 502);
    }

    if (plan is null)
    {
        return Results.Problem("У гостя ще немає кошика — переоптимізовувати нічого.", statusCode: 409);
    }

    var assembler = new BasketAssembler(mcpClient, plan.Session);

    CartState currentCart;
    try
    {
        currentCart = await assembler.GetCartAsync();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося прочитати поточний кошик: {ex.Message}", statusCode: 502);
    }

    // TASKS.md 7.2: "не довіряти success: true" - only real out-of-stock validations trigger a
    // re-solve, never a guessed/assumed one.
    var outOfStockIds = currentCart.Validations.Where(v => v.IsOutOfStock).Select(v => v.ProductId).Distinct().ToList();
    if (outOfStockIds.Count == 0)
    {
        return Results.Ok(new { needsReoptimization = false, message = "Усі позиції в кошику доступні — переоптимізація не потрібна." });
    }

    var mode = plan.Coverage.CoveragePercent >= 60 ? NutritionResolverMode.Exact : NutritionResolverMode.CategoryIndex;
    var nutritionResolver = NutritionResolverFactory.Create(mode, mcpClient, plan.Session);
    var reoptimizer = new ReoptimizationService(mcpClient, nutritionResolver, loggingSolver, assembler, plan.Session);

    ReoptimizationResult result;
    try
    {
        result = await reoptimizer.ReoptimizeAsync(plan.Request, currentCart);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Переоптимізація не вдалась: {ex.Message}", statusCode: 502);
    }

    var oldIds = currentCart.Lines.Select(l => l.ProductId).ToHashSet();
    var newIds = result.FinalCart.Lines.Select(l => l.ProductId).ToHashSet();
    var diffCount = oldIds.Except(newIds).Count() + newIds.Except(oldIds).Count();
    // Best-effort: names for products the reoptimizer added fresh (not in the original pool)
    // aren't resolved here - falls back to null (UI shows the productId), honest rather than an
    // extra round of MCP calls just for display.
    var nameByProductId = plan.Request.Candidates.ToDictionary(c => c.ProductId, c => c.Name);

    return Results.Ok(new
    {
        needsReoptimization = true,
        droppedProductIds = outOfStockIds,
        fullyResolved = result.FullyResolved,
        iterations = result.Iterations,
        degradedNotes = result.DegradedNotes,
        newBasketDiffCount = diffCount,
        lines = result.FinalCart.Lines.Select(l => new { productId = l.ProductId, name = nameByProductId.GetValueOrDefault(l.ProductId), quantity = l.Quantity }),
        totalAfterDiscounts = result.FinalCart.TotalKopecks / 100m,
    });
});

app.MapPost("/api/checkout", async (MakroChefDbContext db, CheckoutRequest? body) =>
{
    var applyBonus = body?.ApplyBonus ?? false;

    // No real user/session model yet (that's section 4 broader work) - a single dev user until then.
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var encryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    var tokenStore = new EfMcpTokenStore(db);
    var stored = await tokenStore.FindByUserAsync(devUserId);
    if (stored is null)
    {
        return Results.Problem("Немає збереженого MCP-токена. Виконайте: dotnet run -- auth", statusCode: 503);
    }

    var tokenEncryptor = new TokenEncryptor(encryptionKey);
    var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
    var recorder = new EfMcpCallRecorder(db);
    await using var mcpClient = new MakroChefMcpClient(mcpBaseUri, new FixedTokenProvider(accessToken), recorder, devUserId);

    SessionContext? session;
    try
    {
        session = await new SessionBootstrap(mcpClient).EnsureAsync();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося прочитати кошик: {ex.Message}", statusCode: 502);
    }

    if (session is null)
    {
        return Results.Problem("У гостя ще немає кошика — оформлення неможливе.", statusCode: 409);
    }

    // BLOCKERS.md B-6: a developer must eye-check the first real checkout link before trusting
    // this cascade blindly - not yet done, so this stays flagged live even though the code path
    // is complete and tested against stub fixtures.
    decimal? bonusOffered = null;
    CheckoutLinks links;
    try
    {
        links = await new CheckoutCascade(mcpClient, session).RunAsync(confirmApplyBonus: amount =>
        {
            bonusOffered = amount;
            return Task.FromResult(applyBonus);
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Каскад чекауту не вдався: {ex.Message}", statusCode: 502);
    }

    return Results.Ok(new
    {
        webLink = links.WebLink,
        mobileLink = links.MobileLink,
        totalAfterDiscounts = links.TotalKopecks / 100m,
        bonusOffered,
        bonusApplied = applyBonus && bonusOffered is not null,
        // B-6 (2026-09-14): there is no real checkout link - the MCP surface has no
        // checkout/place_order/pay tool at all. These are the cart's own validation messages
        // telling the guest why payment can't be finished yet (e.g. an item went out of stock).
        blockingValidations = links.BlockingValidations ?? [],
    });
});

app.MapGet("/api/week-over-week", async (MakroChefDbContext db) =>
{
    // No real user/session model yet (that's section 4 broader work) - a single dev user until then.
    var devUserId = Guid.Parse(Environment.GetEnvironmentVariable("DEV_USER_ID") ?? "00000000-0000-0000-0000-000000000001");
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    var encryptionKey = Environment.GetEnvironmentVariable("TOKEN_ENCRYPTION_KEY") ?? "dev-only-insecure-key";

    var tokenStore = new EfMcpTokenStore(db);
    var stored = await tokenStore.FindByUserAsync(devUserId);
    if (stored is null)
    {
        return Results.Problem("Немає збереженого MCP-токена. Виконайте: dotnet run -- auth", statusCode: 503);
    }

    var tokenEncryptor = new TokenEncryptor(encryptionKey);
    var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
    var recorder = new EfMcpCallRecorder(db);
    await using var mcpClient = new MakroChefMcpClient(mcpBaseUri, new FixedTokenProvider(accessToken), recorder, devUserId);

    WeekOverWeekResult? result;
    try
    {
        result = await new WeekOverWeekAnalyzer(mcpClient).AnalyzeAsync();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Не вдалося порівняти тижні: {ex.Message}", statusCode: 502);
    }

    if (result is null)
    {
        return Results.Problem("У гостя ще немає кошика.", statusCode: 409);
    }

    if (!result.HasEnoughData)
    {
        return Results.Ok(new { hasEnoughData = false });
    }

    return Results.Ok(new
    {
        hasEnoughData = true,
        isRetrospective = true,
        lastWeekGapGrams = result.LastWeekProteinGapGrams,
        thisWeekGapGrams = result.ThisWeekProteinGapGrams,
    });
});

app.MapGet("/health", async (MakroChefDbContext db, BasketSolver solver) =>
{
    string dbStatus;
    try
    {
        dbStatus = await db.Database.CanConnectAsync() ? "ok" : "unreachable";
    }
    catch
    {
        dbStatus = "unreachable";
    }

    var solverStatus = solver.CanInitialize() ? "ok" : "error";

    return Results.Ok(new
    {
        db = dbStatus,
        solver = solverStatus,
        mcp = "unconfigured",
    });
});

app.Run();
return 0;

public partial class Program;

file class FixedTokenProvider(string token) : IMcpAuthTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(token);
    public Task<string?> ForceRefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}

/// <summary>Body of POST /api/basket/apply. ConfirmClear defaults false so a first call against a
/// non-empty cart always stops for confirmation (TASKS.md 7.1) rather than silently clearing.</summary>
public record ApplyBasketRequest(bool ConfirmClear = false);

/// <summary>Body of POST /api/checkout. ApplyBonus defaults false: the first call always runs the
/// cascade without spending bonuses and reports how much was on offer (TASKS.md 7.3 "питати,
/// ніколи не застосовувати мовчки") - the guest confirms, then the Mini App calls again with
/// applyBonus:true to actually spend them.</summary>
public record CheckoutRequest(bool ApplyBonus = false);
