using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Model.Logging;

namespace IntroDbApp.Services
{
    public class IntroDbLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Title { get; set; } = "";
        public string Action { get; set; } = "";
        public string Level { get; set; } = "info"; // "success", "info", "warn", "error"
        public string Details { get; set; } = "";
    }

    public static class IntroDbActivityLogger
    {
        private static readonly ConcurrentQueue<IntroDbLogEntry> _logs = new ConcurrentQueue<IntroDbLogEntry>();
        private const int MaxEntries = 500;
        private static readonly object _fileLock = new object();
        private static bool _initialized = false;
        public static ILogger? SystemLogger { get; set; }

        private static string GetStoragePath()
        {
            try
            {
                if (Plugin.Instance?.AppPaths?.PluginConfigurationsPath != null)
                {
                    return Path.Combine(Plugin.Instance.AppPaths.PluginConfigurationsPath, "introdb_activity.json");
                }
                if (Plugin.Instance?.AppPaths?.ProgramDataPath != null)
                {
                    return Path.Combine(Plugin.Instance.AppPaths.ProgramDataPath, "data", "introdb_activity.json");
                }
            }
            catch { }
            return "/var/lib/emby/plugins/configurations/introdb_activity.json";
        }

        private static void EnsureLoaded()
        {
            if (_initialized) return;
            lock (_fileLock)
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    string path = GetStoragePath();
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var loaded = JsonSerializer.Deserialize<List<IntroDbLogEntry>>(json);
                            if (loaded != null && loaded.Count > 0)
                            {
                                while (_logs.TryDequeue(out _)) { }
                                var toAdd = loaded.TakeLast(MaxEntries);
                                foreach (var item in toAdd)
                                {
                                    _logs.Enqueue(item);
                                }
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    SystemLogger?.Warn($"[IntroDB.app] Error al leer introdb_activity.json: {ex.Message}");
                }

                SeedInitialLogs();
            }
        }

        private static void SeedInitialLogs()
        {
            var config = Plugin.Instance?.Configuration;
            string opMode = config?.OperationMode ?? "Hybrid";

            _logs.Enqueue(new IntroDbLogEntry
            {
                Timestamp = DateTime.Now,
                Title = "Servidor Emby",
                Action = "Servicio Iniciado",
                Level = "info",
                Details = $"IntroDB.app v4.3.0 activo en modo {opMode}. Monitoreo en tiempo real listo."
            });

            if (config != null && !string.IsNullOrWhiteSpace(config.LastScanStatus))
            {
                _logs.Enqueue(new IntroDbLogEntry
                {
                    Timestamp = DateTime.Now.AddSeconds(-2),
                    Title = "Historial de Escaneo",
                    Action = "Último Estado",
                    Level = "info",
                    Details = config.LastScanStatus
                });
            }

            if (config != null && !string.IsNullOrWhiteSpace(config.BackupPath))
            {
                _logs.Enqueue(new IntroDbLogEntry
                {
                    Timestamp = DateTime.Now.AddSeconds(-1),
                    Title = "Sistema de Respaldo",
                    Action = "Directorio Configurado",
                    Level = "info",
                    Details = $"Ruta de respaldo: {config.BackupPath} (Marcadores respaldados: {config.LastBackupCount})"
                });
            }

            SaveToDisk();
        }

        private static void SaveToDisk()
        {
            lock (_fileLock)
            {
                try
                {
                    string path = GetStoragePath();
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var list = _logs.ToList();
                    string json = JsonSerializer.Serialize(list, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(path, json);
                }
                catch (Exception ex)
                {
                    SystemLogger?.Warn($"[IntroDB.app] Error al guardar introdb_activity.json: {ex.Message}");
                }
            }
        }

        public static void Log(string title, string action, string level = "info", string details = "")
        {
            EnsureLoaded();

            var entry = new IntroDbLogEntry
            {
                Timestamp = DateTime.Now,
                Title = title,
                Action = action,
                Level = level,
                Details = details
            };

            _logs.Enqueue(entry);
            while (_logs.Count > MaxEntries && _logs.TryDequeue(out _)) { }

            SaveToDisk();

            if (SystemLogger != null)
            {
                string msg = $"[IntroDB.app] [{action}] {title}{(string.IsNullOrWhiteSpace(details) ? "" : $" -> {details}")}";
                switch (level.ToLowerInvariant())
                {
                    case "error":
                        SystemLogger.Error(msg);
                        break;
                    case "warn":
                        SystemLogger.Warn(msg);
                        break;
                    default:
                        SystemLogger.Info(msg);
                        break;
                }
            }
        }

        public static List<IntroDbLogEntry> GetRecentLogs()
        {
            EnsureLoaded();
            return _logs.Reverse().ToList();
        }

        public static void Clear()
        {
            EnsureLoaded();
            lock (_fileLock)
            {
                while (_logs.TryDequeue(out _)) { }
                try
                {
                    string path = GetStoragePath();
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch { }
            }
        }
    }
}
