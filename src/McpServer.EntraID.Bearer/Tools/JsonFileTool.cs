using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace McpServer.EntraID.Bearer.Tools;

[McpServerToolType]
public class JsonFileTool
{
    [McpServerTool, Description("Parse and print JSON content provided as input.")]
    public static Task<string> ParseJson(
        [Description("The JSON content to parse and print.")] string jsonContent)
    {
        try
        {
            // Parse and re-serialize with indentation for readability
            var jsonDocument = JsonDocument.Parse(jsonContent);
            var formattedJson = JsonSerializer.Serialize(jsonDocument.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                MaxDepth = 64
            });

            return Task.FromResult(formattedJson);
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"Error: Invalid JSON format - {ex.Message}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error parsing JSON: {ex.Message}");
        }
    }
}
