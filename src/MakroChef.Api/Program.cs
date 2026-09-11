using MakroChef.Api.Commands;
using MakroChef.Data;
using MakroChef.Solver;
using Microsoft.EntityFrameworkCore;

if (args is ["auth"])
{
    var mcpBaseUri = new Uri(Environment.GetEnvironmentVariable("MCP_BASE_URI") ?? "https://mcp.silpo.ua/mcp");
    return await AuthCommand.RunAsync(mcpBaseUri);
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
