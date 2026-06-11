using System.Text.Json;
using System.Text.Json.Serialization;

namespace RGTools.App.Core;

[JsonSerializable(typeof(ToolManifest))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
internal partial class ToolsJsonContext : JsonSerializerContext
{
}
