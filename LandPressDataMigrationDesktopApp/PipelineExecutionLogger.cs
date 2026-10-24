using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using WpfApp = System.Windows.Application;

namespace LandPressDataMigrationDesktopApp
{
    public enum PipelineLogSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Represents an individual structured log entry for execution events, errors, and font mapping anomalies.
    /// </summary>
    public class PipelineLogEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string TimeFormatted => Timestamp.ToString("HH:mm:ss");
        public string Phase { get; set; } = "";
        public PipelineLogSeverity Severity { get; set; } = PipelineLogSeverity.Info;
        public string Table { get; set; } = "";
        public string Column { get; set; } = "";
        public string RecordId { get; set; } = "";
        public string Message { get; set; } = "";
        public string? RawValue { get; set; }
        public string? ConvertedValue { get; set; }

        public string SeverityIcon => Severity switch
        {
            PipelineLogSeverity.Error   => "❌",
            PipelineLogSeverity.Warning => "⚠️",
            _                           => "ℹ️"
        };

        public string SeverityColor => Severity switch
        {
            PipelineLogSeverity.Error   => "#EF4444",
            PipelineLogSeverity.Warning => "#F59E0B",
            _                           => "#38BDF8"
        };
    }

    /// <summary>
    /// Centralized in-memory execution and mapping logger for the DLRS data migration pipeline.
    /// Captures breaking exceptions, SQL errors, and character mapping issues for display in the Log View screen.
    /// </summary>
    public static class PipelineExecutionLogger
    {
        private static int _nextId = 1;
        private static readonly object _lock = new();

        public static ObservableCollection<PipelineLogEntry> Entries { get; } = new();

        public static event Action? LogsUpdated;

        public static void Clear()
        {
            lock (_lock)
            {
                _nextId = 1;
                WpfApp.Current?.Dispatcher.Invoke(() => Entries.Clear());
            }
            LogsUpdated?.Invoke();
        }

        public static void LogInfo(string phase, string message, string table = "", string column = "", string recordId = "")
        {
            AddEntry(new PipelineLogEntry
            {
                Phase = phase,
                Severity = PipelineLogSeverity.Info,
                Message = message,
                Table = table,
                Column = column,
                RecordId = recordId
            });
        }

        public static void LogWarning(string phase, string message, string table = "", string column = "", string recordId = "", string? raw = null, string? converted = null)
        {
            AddEntry(new PipelineLogEntry
            {
                Phase = phase,
                Severity = PipelineLogSeverity.Warning,
                Message = message,
                Table = table,
                Column = column,
                RecordId = recordId,
                RawValue = raw,
                ConvertedValue = converted
            });
        }

        public static void LogError(string phase, string message, string table = "", string column = "", string recordId = "", string? raw = null)
        {
            AddEntry(new PipelineLogEntry
            {
                Phase = phase,
                Severity = PipelineLogSeverity.Error,
                Message = message,
                Table = table,
                Column = column,
                RecordId = recordId,
                RawValue = raw
            });
        }

        /// <summary>
        /// Analyzes a converted string value and logs an anomaly if unmapped characters, fallback markers,
        /// or unexpected data truncations are detected.
        /// </summary>
        public static bool CheckAndLogMappingAnomaly(
            string phase,
            string table,
            string column,
            string recordId,
            string? rawValue,
            string? convertedValue)
        {
            if (string.IsNullOrEmpty(rawValue)) return false;

            bool hasIssue = false;
            var issues = new List<string>();

            // Check for fallback unmapped character '×' produced by Adarshalipi conversion map
            if (convertedValue != null && convertedValue.Contains("×"))
            {
                hasIssue = true;
                issues.Add("Unmapped glyph marker '×' detected in converted text");
            }

            // Check if input had characters but output ended up empty
            if (!string.IsNullOrWhiteSpace(rawValue) && string.IsNullOrWhiteSpace(convertedValue))
            {
                hasIssue = true;
                issues.Add("Converted value resulted in an empty string");
            }

            // Check for unmapped high-ASCII control characters
            if (convertedValue != null)
            {
                for (int i = 0; i < convertedValue.Length; i++)
                {
                    char c = convertedValue[i];
                    if (c > 127 && (c < '\u0980' || c > '\u09FF') && c != '×')
                    {
                        hasIssue = true;
                        issues.Add($"Unconverted non-Bengali code '\\u{(int)c:X4}' found");
                        break;
                    }
                }
            }

            if (hasIssue)
            {
                LogWarning(
                    phase,
                    string.Join("; ", issues),
                    table: table,
                    column: column,
                    recordId: recordId,
                    raw: rawValue,
                    converted: convertedValue);
            }

            return hasIssue;
        }

        private static void AddEntry(PipelineLogEntry entry)
        {
            lock (_lock)
            {
                entry.Id = _nextId++;
                if (WpfApp.Current != null && WpfApp.Current.Dispatcher != null)
                {
                    if (WpfApp.Current.Dispatcher.CheckAccess())
                    {
                        Entries.Add(entry);
                    }
                    else
                    {
                        WpfApp.Current.Dispatcher.BeginInvoke(new Action(() => Entries.Add(entry)));
                    }
                }
                else
                {
                    Entries.Add(entry);
                }
            }
            LogsUpdated?.Invoke();
        }

        public static int ErrorCount => Entries.Count(e => e.Severity == PipelineLogSeverity.Error);
        public static int WarningCount => Entries.Count(e => e.Severity == PipelineLogSeverity.Warning);
        public static int TotalCount => Entries.Count;

        /// <summary>
        /// Exports all logged entries to a CSV file.
        /// </summary>
        public static void ExportToCsv(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ID,Timestamp,Phase,Severity,Table,Column,RecordId,Message,RawValue,ConvertedValue");

            lock (_lock)
            {
                foreach (var e in Entries)
                {
                    sb.AppendLine(
                        $"{e.Id}," +
                        $"\"{e.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                        $"\"{Escape(e.Phase)}\"," +
                        $"\"{e.Severity}\"," +
                        $"\"{Escape(e.Table)}\"," +
                        $"\"{Escape(e.Column)}\"," +
                        $"\"{Escape(e.RecordId)}\"," +
                        $"\"{Escape(e.Message)}\"," +
                        $"\"{Escape(e.RawValue)}\"," +
                        $"\"{Escape(e.ConvertedValue)}\"");
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\"", "\"\"");
        }
    }
}
