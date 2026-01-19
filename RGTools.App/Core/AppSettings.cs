namespace RGTools.App.Core;

public record AppSettings
{
  public bool DnsGuardianEnabled { get; init; } = true;
}
