using System.Diagnostics;
using System.Windows;

namespace RGTools.App.Core;

public class JumpboxService(ConfigService config)
{
    public async Task LaunchAsync()
    {
        string? path = config.Current.JumboxFolderPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            path = PromptForPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            await config.SaveAsync(config.Current with { JumboxFolderPath = path });
        }

        if (!await ValidateWslEnvironmentAsync(path)) return;

        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            Arguments = $"--cd {path} zsh -i -c \"uv run python3 jumbox.py\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        using var process = Process.Start(psi);
        LogService.Log($"[JUMPBOX] Process detached and memory released for path: {path}");
    }

    private string? PromptForPath()
    {
        string example = "/home/mario/code/github_work/jumbox";

        return Microsoft.VisualBasic.Interaction.InputBox(
            $"Ingrese la ruta de WSL2 para Jumpbox:\nEjemplo: {example}",
            "Configuración Requerida",
            example);
    }

    private async Task<bool> ValidateWslEnvironmentAsync(string path)
    {
        try
        {
            LogService.Log($"[JUMPBOX] Pre-flight check in: {path}");

            // Simplified: check for script and .venv directory only
            var checkCommand = $"[ -f jumbox.py ] || (echo 'MISSING_FILE' && exit 1); " +
                               $"[ -d .venv ] || (echo 'MISSING_VENV' && exit 2)";

            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = $"--cd {path} zsh -i -c \"{checkCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                LogService.Log($"[JUMPBOX] Validation Failed | Code: {process.ExitCode}");
                if (!string.IsNullOrEmpty(stdout)) LogService.Log($"[JUMPBOX-STDOUT] {stdout.Trim()}");
                if (!string.IsNullOrEmpty(stderr)) LogService.Log($"[JUMPBOX-STDERR] {stderr.Trim()}");

                string detail = process.ExitCode switch
                {
                    1 => "No se encontró jumbox.py en la ruta especificada.",
                    2 => "No se encontró la carpeta .venv. Asegúrese de haberla creado en WSL2.",
                    _ => "Fallo de comunicación con WSL2."
                };

                MessageBox.Show($"Error de validación:\n\n{detail}",
                    "Jumpbox Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            LogService.Log("[JUMPBOX] Basic validation passed.");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log("[JUMPBOX] Validation exception", ex);
            return false;
        }
    }
}
