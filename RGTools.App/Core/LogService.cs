using System.Diagnostics;
using System.IO;
using System.Text;

namespace RGTools.App.Core;

public static class LogService
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RGTools",
        "logs");

    private static readonly string LogPath = Path.Combine(LogDir, "rgtools.log");
    private static readonly string BackupPath = Path.Combine(LogDir, "rgtools.bak"); // Archivo de respaldo

    // Lock ligero para evitar conflictos si múltiples hilos loguean a la vez
    private static readonly Lock _lockObj = new();

    public static void Initialize()
    {
        try
        {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);

            RotateLogsIfNeeded();

            Log("=== RGTools Service Started ===");
        }
        catch (Exception ex)
        {
            // Fallback de emergencia a la consola de debug si falla el disco
            Debug.WriteLine($"[CRITICAL] LogService init failed: {ex.Message}");
        }
    }

    public static void Log(string message, Exception? ex = null)
    {
        // Usamos la nueva característica 'Lock' de .NET 9/10 que es más eficiente que 'object'
        lock (_lockObj)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var sb = new StringBuilder();

                sb.Append($"[{timestamp}] {message}");

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append($"   >>> Error: {ex.Message}");
                    // Opcional: StackTrace solo si es necesario, consume string alloc
                    // sb.Append($"\n   {ex.StackTrace}");
                }

                var finalLine = sb.ToString();

                // 1. Escritura en Disco (Robusta, funciona siempre)
                // AppendAllText abre, escribe y cierra. Es más lento que un stream abierto,
                // pero mucho más seguro para una app que puede ser matada abruptamente.
                // Dado el volumen bajo de logs, el impacto en CPU es despreciable.
                File.AppendAllText(LogPath, finalLine + Environment.NewLine);

                // 2. Escritura en Output de VS (Para desarrollo)
                Debug.WriteLine(finalLine);
            }
            catch
            {
                // Silencio total. El logger nunca debe tumbar la app.
            }
        }
    }

    private static void RotateLogsIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            // Limite de 2MB
            if (info.Exists && info.Length > 2 * 1024 * 1024)
            {
                // Si existe un backup anterior, lo borramos
                if (File.Exists(BackupPath))
                    File.Delete(BackupPath);

                // Movemos el log actual a backup (rotación simple)
                File.Move(LogPath, BackupPath);
            }
        }
        catch { /* Ignorar errores de rotación */ }
    }

    // Helpers rápidos
    public static string GetLogPath() => LogPath;
}
