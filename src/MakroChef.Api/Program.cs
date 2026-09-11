using MakroChef.Api.Commands;
using MakroChef.Data;
using MakroChef.Mcp.OAuth;
using MakroChef.Solver;
using Microsoft.EntityFrameworkCore;

if (args is ["auth"])
{
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    return await AuthCommand.RunAsync(mcpBaseUri);
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
