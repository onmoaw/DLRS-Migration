using Microsoft.Win32;
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Interaction logic for IntermediateDbLogsDialog.xaml
    /// Displays audit logs from [DLRS_Migration_Intrm].dbo.tblUnitMigrationLog.
    /// </summary>
    public partial class IntermediateDbLogsDialog : Window
    {
        private readonly string _masterConnectionString;
        private DataTable? _logsTable;

        public IntermediateDbLogsDialog(string masterConnectionString)
        {
            InitializeComponent();
            _masterConnectionString = masterConnectionString;

            UpdateThemeButton();
            ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;

            Loaded += async (s, e) => await LoadLogsAsync();
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
        }

        private void OnThemeChanged(AppTheme theme)
        {
            UpdateThemeButton();
        }

        private void UpdateThemeButton()
        {
            if (txtThemeIcon == null || txtThemeLabel == null) return;
            bool isDark = ThemeManager.CurrentTheme == AppTheme.Dark;
            txtThemeIcon.Text = isDark ? "🌙" : "☀️";
            txtThemeLabel.Text = isDark ? "Dark" : "Light";
        }

        private async Task LoadLogsAsync()
        {
            txtLoading.Visibility = Visibility.Visible;
            txtEmpty.Visibility = Visibility.Collapsed;
            dgLogs.Visibility = Visibility.Collapsed;

            try
            {
                _logsTable = await Task.Run(() => IntermediateDbService.GetMigrationLogsAsync(_masterConnectionString));

                if (_logsTable == null || _logsTable.Rows.Count == 0)
                {
                    txtLoading.Visibility = Visibility.Collapsed;
                    txtEmpty.Visibility = Visibility.Visible;
                    txtTotalRuns.Text = "0";
                    txtTotalUnits.Text = "0";
                    txtTotalRecords.Text = "0";
                    txtLatestRun.Text = "-";
                    return;
                }

                // Compute stats
                int totalRuns = _logsTable.Rows.Count;
                var distinctUnits = _logsTable.AsEnumerable()
                    .Select(r => r.Field<string>("Unit Code") ?? string.Empty)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct()
                    .Count();

                long totalRecords = _logsTable.AsEnumerable()
                    .Sum(r => Convert.ToInt64(r["Total Records"] == DBNull.Value ? 0 : r["Total Records"]));

                string latestRun = _logsTable.Rows[0]["Completed At"]?.ToString() ?? "-";

                txtTotalRuns.Text = totalRuns.ToString("N0");
                txtTotalUnits.Text = distinctUnits.ToString("N0");
                txtTotalRecords.Text = totalRecords.ToString("N0");
                txtLatestRun.Text = latestRun;

                dgLogs.ItemsSource = _logsTable.DefaultView;
                txtLoading.Visibility = Visibility.Collapsed;
                dgLogs.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                txtLoading.Text = $"Error loading logs: {ex.Message}";
                txtLoading.Visibility = Visibility.Visible;
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadLogsAsync();
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_logsTable == null || _logsTable.Rows.Count == 0)
            {
                MessageBox.Show("No log entries to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var sfd = new SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    FileName = $"DLRS_Migration_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    Title = "Export Migration Logs to CSV"
                };

                if (sfd.ShowDialog() == true)
                {
                    var sb = new StringBuilder();

                    // Column headers
                    var colNames = _logsTable.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\"");
                    sb.AppendLine(string.Join(",", colNames));

                    // Rows
                    foreach (DataRow row in _logsTable.Rows)
                    {
                        var fields = row.ItemArray.Select(field =>
                        {
                            string val = field?.ToString()?.Replace("\"", "\"\"") ?? string.Empty;
                            return $"\"{val}\"";
                        });
                        sb.AppendLine(string.Join(",", fields));
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);

                    MessageBox.Show(
                        $"Exported {_logsTable.Rows.Count} records successfully to:\n{sfd.FileName}",
                        "Export Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenPipelineLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = PipelineExecutionLogger.TodayLogFilePath;
                string dir = PipelineExecutionLogger.LogsDirectory;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, $"--- Pipeline Execution Log ({DateTime.Now:yyyy-MM-dd}) ---" + Environment.NewLine, Encoding.UTF8);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open log file:\n{ex.Message}", "Open Log File", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
