using System.Text.Json.Serialization;

namespace RGTools.App.Core;

/// <summary>
/// Represents the persistent configuration state of the application.
/// Uses C# records for immutability and value equality.
/// </summary>
public record AppSettings
{
  /// <summary>
  /// If true, the VPN Guardian service is active and monitoring.
  /// </summary>
  public bool VpnGuardianEnabled { get; init; } = false;

  /// <summary>
  /// If true, the DNS Enforcer is active preventing leaks.
  /// </summary>
  public bool DnsGuardianEnabled { get; init; } = false;

  /// <summary>
  /// Last saved window position (X). Optional.
  /// </summary>
  public double? WindowLeft { get; init; }

  /// <summary>
  /// Last saved window position (Y). Optional.
  /// </summary>
  public double? WindowTop { get; init; }
}
