using MakroChef.Agent.Coverage;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Mcp.OAuth;

namespace MakroChef.Api.Commands;

/// <summary>`dotnet run -- probe` — TASKS.md 3.4. Needs a live MCP token (BLOCKERS.md B-2);
/// without one it fails with one clear line, not a stack trace.</summary>
public static class ProbeCommand
{
    public static async Task<int> RunAsync(Uri mcpBaseUri, Guid userId, MakroChefDbContext db, EfMcpTokenStore tokenStore, TokenEncryptor tokenEncryptor)
    {
        try
        {
            var stored = await tokenStore.FindByUserAsync(userId);
            if (stored is null)
            {
                Console.Error.WriteLine("Немає збереженого MCP-токена. Спочатку виконайте: dotnet run -- auth");
                return 1;
            }

            var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
            var staticTokenProvider = new StaticTokenProvider(accessToken);

            var recorder = new EfMcpCallRecorder(db);
            await using var mcpClient = new CachingMcpClient(
                new MakroChefMcpClient(mcpBaseUri, staticTokenProvider, recorder, userId),
                new CachingMcpClientOptions());

            Console.WriteLine("Читаю історію покупок і деталі товарів (це не миттєво — кожен унікальний SKU це окремий виклик)...");
            var report = await new CoverageProbe(mcpClient).RunAsync();

            var reportPath = Path.Combine(RepoPaths.FindRoot(), "docs", "coverage-report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, report.ToMarkdown());

            Console.WriteLine($"Готово. Звіт: {reportPath}");
            Console.WriteLine();
            Console.WriteLine(report.ToMarkdown());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Проба покриття не вдалась: {ex.Message}");
            return 1;
        }
    }

    private class StaticTokenProvider(string token) : IMcpAuthTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(token);

        public Task<string?> ForceRefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
