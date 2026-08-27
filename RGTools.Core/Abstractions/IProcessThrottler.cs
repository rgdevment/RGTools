namespace RGTools.App.Core;

public interface IProcessThrottler
{
    void SetEfficiency(string processName, bool enabled);
}
