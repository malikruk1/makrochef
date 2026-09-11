namespace MakroChef.Domain.Mcp;

/// <summary>A tool as reported by the server's own tools/list — names, arguments and JSON
/// Schema always come from here, never hardcoded (TASKS.md 3.1).</summary>
public record McpToolDescriptor(string Name, string? Description, string InputSchemaJson);
