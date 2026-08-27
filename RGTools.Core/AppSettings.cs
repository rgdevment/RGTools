using System.Text.Json;
using System.Text.Json.Serialization;

namespace RGTools.App.Core;

[JsonConverter(typeof(ProfileKindConverter))]
public enum ProfileKind
{
    Balanced,
    Work,
    Gaming
}

// Falls back instead of throwing: a config written by an older version carries "Boost", and a throw
// here makes ConfigService reset every other setting (DNS, startup, tool roots, consent).
public sealed class ProfileKindConverter : JsonConverter<ProfileKind>
{
    public override ProfileKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String
           && Enum.TryParse(reader.GetString(), ignoreCase: true, out ProfileKind kind)
            ? kind
            : ProfileKind.Balanced;

    public override void Write(Utf8JsonWriter writer, ProfileKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

public record AppSettings
{
    public bool DnsGuardianEnabled { get; init; } = true;
    public bool StartWithWindows { get; init; } = false;
    public ProfileKind ActiveProfile { get; init; } = ProfileKind.Balanced;
    public bool PowerMigrationDone { get; init; }
    public ConsentSettings Consent { get; init; } = new();

    public string? JumboxFolderPath { get; init; }

    public string[]? ToolRoots { get; init; }
}

public record ConsentSettings
{
    public Dictionary<string, bool> Granted { get; init; } = new();
}
