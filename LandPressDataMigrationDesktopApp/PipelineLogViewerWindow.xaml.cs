using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Win32SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfMessageBox = System.Windows.MessageBox;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Interaction logic for PipelineLogViewerWindow.xaml
    /// </summary>
    public partial class PipelineLogViewerWindow : Window
    {
        private ICollectionView? _logsView;

        public PipelineLogViewerWindow()
        {
            InitializeComponent();

            _logsView = CollectionViewSource.GetDefaultView(PipelineExecutionLogger.Entries);
            if (_logsView != null)
            {
                _logsView.Filter = FilterLogs;
                gridLogs.ItemsSource = _logsView;
            }

            UpdateMetrics();
            PipelineExecutionLogger.LogsUpdated += OnLogsUpdated;
            Closed += (s, e) => PipelineExecutionLogger.LogsUpdated -= OnLogsUpdated;
        }

        private void OnLogsUpdated()
        {
            Dispatcher.Invoke(() =>
            {
                UpdateMetrics();
                _logsView?.Refresh();
            });
        }

        private void UpdateMetrics()
        {
            txtCountTotal.Text = $"Total: {PipelineExecutionLogger.TotalCount}";
            txtCountErrors.Text = $"❌ Errors: {PipelineExecutionLogger.ErrorCount}";
            txtCountWarnings.Text = $"⚠️ Warnings: {PipelineExecutionLogger.WarningCount}";
            txtCountInfo.Text = $"ℹ Info: {PipelineExecutionLogger.TotalCount - PipelineExecutionLogger.ErrorCount - PipelineExecutionLogger.WarningCount}";
        }

        private bool FilterLogs(object obj)
        {
            if (obj is not PipelineLogEntry entry) return false;

            // 1. Severity Filter
            if (cmbSeverityFilter != null && cmbSeverityFilter.SelectedIndex > 0)
            {
                switch (cmbSeverityFilter.SelectedIndex)
                {
                    case 1: // Errors Only
                        if (entry.Severity != PipelineLogSeverity.Error) return false;
                        break;
                    case 2: // Warnings & Errors
                        if (entry.Severity == PipelineLogSeverity.Info) return false;
                        break;
                    case 3: // Info Only
                        if (entry.Severity != PipelineLogSeverity.Info) return false;
                        break;
                }
            }

            // 2. Phase Filter
            if (cmbPhaseFilter != null && cmbPhaseFilter.SelectedIndex > 0)
            {
                string selectedPhase = ((ComboBoxItem)cmbPhaseFilter.SelectedItem).Content.ToString() ?? "";
                if (!string.IsNullOrEmpty(selectedPhase) && !entry.Phase.Contains(selectedPhase, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 3. Text Search Filter
            if (txtSearchFilter != null && !string.IsNullOrWhiteSpace(txtSearchFilter.Text))
            {
                string query = txtSearchFilter.Text.Trim();
                bool matches =
                    (entry.Message != null && entry.Message.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (entry.Table != null && entry.Table.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (entry.Column != null && entry.Column.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (entry.RecordId != null && entry.RecordId.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (entry.RawValue != null && entry.RawValue.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (entry.ConvertedValue != null && entry.ConvertedValue.Contains(query, StringComparison.OrdinalIgnoreCase));

                if (!matches) return false;
            }

            return true;
        }

        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _logsView?.Refresh();
        }

        private void CmbSeverityFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _logsView?.Refresh();
        }

        private void CmbPhaseFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _logsView?.Refresh();
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new Win32SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"DLRS_Pipeline_Log_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    PipelineExecutionLogger.ExportToCsv(sfd.FileName);
                    WpfMessageBox.Show($"Logs successfully exported to:\n{sfd.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show($"Failed to export logs: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnOpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = PipelineExecutionLogger.TodayLogFilePath;
                string dir = PipelineExecutionLogger.LogsDirectory;
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                if (!System.IO.File.Exists(path))
                {
                    System.IO.File.WriteAllText(path, $"--- Pipeline Execution Log ({DateTime.Now:yyyy-MM-dd}) ---" + Environment.NewLine, System.Text.Encoding.UTF8);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Could not open log file:\n{ex.Message}", "Open Log File", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            var res = WpfMessageBox.Show("Are you sure you want to clear all log entries?", "Confirm Clear Logs", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                PipelineExecutionLogger.Clear();
                UpdateMetrics();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Displays the Pipeline Log Viewer window.
        /// </summary>
        public static void ShowLogViewer(Window? owner = null)
        {
            var win = new PipelineLogViewerWindow();
            if (owner != null && owner.IsLoaded)
                win.Owner = owner;
            win.Show();
        }
    }
}
