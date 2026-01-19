using System.Diagnostics;

namespace RGTools.App.Core;

public class DnsGuardianService
{
  private CancellationTokenSource? _cts;

  public void Start()
  {
    if (_cts != null) return;

    _cts = new CancellationTokenSource();

    Task.Run(() => LoopAsync(_cts.Token));

    Debug.WriteLine("[Guardian] Started.");
  }

  public void Stop()
  {
    if (_cts == null) return;

    _cts.Cancel(); // Request cancellation
    _cts.Dispose();
    _cts = null;

    Debug.WriteLine("[Guardian] Stopped.");
  }

  private async Task LoopAsync(CancellationToken token)
  {
    try
    {
      while (!token.IsCancellationRequested)
      {
        Debug.WriteLine($"[Guardian] Scanning... {DateTime.Now:T}");

        await Task.Delay(5000, token);
      }
    }
    catch (OperationCanceledException)
    {
      //disable guardian cleanly
      Debug.WriteLine("[Guardian] Cancellation requested, exiting loop.");
    }
    catch (Exception ex)
    {
      Debug.WriteLine($"[Guardian] CRITICAL ERROR: {ex.Message}");
    }
  }
}
