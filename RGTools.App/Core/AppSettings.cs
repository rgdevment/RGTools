using System.Text.Json.Serialization;

namespace RGTools.App.Core;

public record AppSettings
{
  public bool DnsGuardianEnabled { get; init; } = true;
  public string? CopilotFolderPath { get; init; }
}

public record LmStudioResponse(
    [property: JsonPropertyName("data")] List<LmModelData> Data
);

public record LmModelData(
    [property: JsonPropertyName("id")] string Id
);

public record LmStudioChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<LmMessage> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens
);

public record LmMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);
