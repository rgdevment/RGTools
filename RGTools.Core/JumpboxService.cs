using System.Diagnostics;

namespace RGTools.App.Core;

public sealed class JumpboxService : IJumpboxService
{
    public async Task<JumpboxResult> LaunchAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new JumpboxResult(false, "Ruta WSL2 no especificada.");

        var validation = await ValidateWslEnvironmentAsync(path);
        if (!validation.Success) return validation;

        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        AddWslArgs(psi, path, "uv run python3 jumbox.py");

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return new JumpboxResult(false, "No se pudo iniciar WSL2.");

            LogService.Log($"[JUMPBOX] WSL2 launched for path: {path}");
            return new JumpboxResult(true);
        }
        catch (Exception ex)
        {
            LogService.Log("[JUMPBOX] Launch failed", ex);
            return new JumpboxResult(false, "No se pudo iniciar WSL2.");
        }
    }

    private static void AddWslArgs(ProcessStartInfo psi, string path, string command)
    {
        psi.ArgumentList.Add("--cd");
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("zsh");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
    }

    private static async Task<JumpboxResult> ValidateWslEnvironmentAsync(string path)
    {
        try
        {
            LogService.Log($"[JUMPBOX] Pre-flight check in: {path}");

            var checkCommand = "[ -f jumbox.py ] || (echo 'MISSING_FILE' && exit 1); " +
                               "[ -d .venv ] || (echo 'MISSING_VENV' && exit 2)";

            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            AddWslArgs(psi, path, checkCommand);

            using var process = Process.Start(psi);
            if (process == null) return new JumpboxResult(false, "No se pudo iniciar WSL2.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string stdout = await stdoutTask;
            string stderr = await stderrTask;

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

                return new JumpboxResult(false, detail);
            }

            LogService.Log("[JUMPBOX] Basic validation passed.");
            return new JumpboxResult(true);
        }
        catch (Exception ex)
        {
            LogService.Log("[JUMPBOX] Validation exception", ex);
            return new JumpboxResult(false, "Error al validar el entorno WSL2.");
        }
    }
}
