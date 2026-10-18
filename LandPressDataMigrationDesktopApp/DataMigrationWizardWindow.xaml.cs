using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MediaColor   = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using SolidBrush   = System.Windows.Media.SolidColorBrush;
using Button       = System.Windows.Controls.Button;
using DataGrid     = System.Windows.Controls.DataGrid;
using TextBox      = System.Windows.Controls.TextBox;
using TextBlock    = System.Windows.Controls.TextBlock;
using StackPanel   = System.Windows.Controls.StackPanel;
using Border       = System.Windows.Controls.Border;
using TextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;
using MessageBox   = System.Windows.MessageBox;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Step-by-step Data Migration Wizard for executing SP_LIS_DATA_MIGRATION in DLRSLSINF_4103PRSDB.
    /// Source DB name is strictly read-only.
    /// </summary>
    public partial class DataMigrationWizardWindow : Window
    {
        private readonly string _targetConnectionString;
        private readonly string _sourceDbName;

        private int _currentStep = 1;
        private const int TotalSteps = 7;

        private Border[] _stepPanels = null!;
        private Border[] _stepDots   = null!;

        // 1-based tracking flags (index 0 unused)
        private readonly bool[] _stepCompleted = new bool[TotalSteps + 1];
        private readonly bool[] _stepSkipped   = new bool[TotalSteps + 1];

        /// <summary>
        /// Fires immediately when all required migration steps (Steps 3, 4, 5) complete.
        /// </summary>
        public event Action? MigrationCompleted;

        /// <summary>
        /// Returns true if all core required migration steps (Steps 3, 4, 5) were completed.
        /// Step 3 = Khatians (MTO_LINFO00)
        /// Step 4 = Plots (MTO_LINFO01)
        /// Step 5 = Owners (MTO_LINFO02)
        /// </summary>
        public bool IsMigrationCompleted =>
            _stepCompleted != null &&
            _stepCompleted[3] &&
            _stepCompleted[4] &&
            _stepCompleted[5];
        private readonly bool[] _stepRequired  = new bool[TotalSteps + 1]
        {
            false,
            false, // Step 1: MTO_AREAINF (Optional)
            false, // Step 2: MANAGE_MISSMATCH (Optional)
            true,  // Step 3: MTO_LINFO00 (Required)
            true,  // Step 4: MTO_LINFO01 (Required)
            true,  // Step 5: MTO_LINFO02 (Required)
            false, // Step 6: UPDTMAPINF01 (Optional)
            false  // Step 7: MTO_PLOTINF01 (Optional)
        };

        public DataMigrationWizardWindow(string targetConnectionString, string sourceDbName)
        {
            InitializeComponent();

            _targetConnectionString = targetConnectionString;
            _sourceDbName           = sourceDbName;

            txtSourceDbDisplay.Text = string.IsNullOrEmpty(sourceDbName) ? "(None Selected)" : sourceDbName;

            _stepPanels = new Border[]
            {
                null!,
                stepPanel1,
                stepPanel2,
                stepPanel3,
                stepPanel4,
                stepPanel5,
                stepPanel6,
                stepPanel7
            };

            InitializeInputs();
            BuildStepIndicators();
            RefreshNav();
            UpdateThemeButton();
            ApplyRoleView();

            ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;
        }

        private void InitializeInputs()
        {
            // Set read-only labels for source DB
            txtS1_Desc01.Text = _sourceDbName;
            txtS2_Desc01.Text = _sourceDbName;
            txtS3_Desc01.Text = _sourceDbName;
            txtS4_Desc01.Text = _sourceDbName;
            txtS5_Desc01.Text = _sourceDbName;
            txtS6_Desc01.Text = _sourceDbName;
            txtS7_Desc01.Text = _sourceDbName;

            string defaultRs = "201901";
            if (txtGlobalDesc02 != null && !string.IsNullOrWhiteSpace(txtGlobalDesc02.Text))
                defaultRs = txtGlobalDesc02.Text.Trim();

            if (txtS3_Desc02 != null && string.IsNullOrEmpty(txtS3_Desc02.Text)) txtS3_Desc02.Text = defaultRs;
            if (txtS4_Desc02 != null && string.IsNullOrEmpty(txtS4_Desc02.Text)) txtS4_Desc02.Text = defaultRs;
            if (txtS5_Desc02 != null && string.IsNullOrEmpty(txtS5_Desc02.Text)) txtS5_Desc02.Text = defaultRs;
            if (txtS7_Desc02 != null && string.IsNullOrEmpty(txtS7_Desc02.Text)) txtS7_Desc02.Text = defaultRs;
        }

        private void ApplyRoleView()
        {
            if (!UserSession.IsAdmin)
            {
                // Hide technical stored procedure details and RS input
                badgeSpName.Visibility = Visibility.Collapsed;
                panelComCod.Visibility = Visibility.Collapsed;
                panelGlobalDesc02.Visibility = Visibility.Collapsed;

                if (panelS3_Desc02 != null) panelS3_Desc02.Visibility = Visibility.Collapsed;
                if (panelS4_Desc02 != null) panelS4_Desc02.Visibility = Visibility.Collapsed;
                if (panelS5_Desc02 != null) panelS5_Desc02.Visibility = Visibility.Collapsed;
                if (panelS7_Desc02 != null) panelS7_Desc02.Visibility = Visibility.Collapsed;

                txtWizardTitle.Text = "🚀 Data Migration Pipeline";
                txtDbLabel.Text = "Target System: Ready for Migration";

                // Human-readable titles & descriptions for standard operators
                txtS1_Title.Text = "Step 1 — Prepare Area Information";
                txtS1_Desc.Text = "Prepares and imports area and mauza boundaries from the source database.";
                btnRunS1.Content = "▶ Import Area Info";

                txtS2_Title.Text = "Step 2 — Verify Khatians & Check Mismatches";
                txtS2_Desc.Text = "Checks and resolves mismatched Khatian records between source tables to ensure data integrity.";
                btnRunS2.Content = "▶ Verify Khatians";

                txtS3_Title.Text = "Step 3 — Import Khatian Records (Required)";
                txtS3_Desc.Text = "Migrates Khatian header records into the target system. This step is mandatory.";
                btnRunS3.Content = "▶ Import Khatian Records";

                txtS4_Title.Text = "Step 4 — Import Plot Details (Required)";
                txtS4_Desc.Text = "Migrates land plot details and boundary information. This step is mandatory.";
                btnRunS4.Content = "▶ Import Plot Details";

                txtS5_Title.Text = "Step 5 — Import Land Ownership (Required)";
                txtS5_Desc.Text = "Migrates owner shares, names, and relationship records. This step is mandatory.";
                btnRunS5.Content = "▶ Import Land Owners";

                txtS6_Title.Text = "Step 6 — Update Map Unit Codes";
                txtS6_Desc.Text = "Updates mapping references and administrative unit codes for spatial alignment.";
                btnRunS6.Content = "▶ Update Map Data";

                txtS7_Title.Text = "Step 7 — Import Detailed Plot Attributes";
                txtS7_Desc.Text = "Imports supplemental plot attributes and survey characteristics.";
                btnRunS7.Content = "▶ Import Plot Attributes";
            }
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
        }

        private void OnThemeChanged(AppTheme theme)
        {
            UpdateThemeButton();
            RefreshStepDots();
        }

        private void UpdateThemeButton()
        {
            if (txtThemeIcon == null || txtThemeLabel == null) return;
            bool isDark = ThemeManager.CurrentTheme == AppTheme.Dark;
            txtThemeIcon.Text = isDark ? "🌙" : "☀️";
            txtThemeLabel.Text = isDark ? "Dark" : "Light";
        }

        private void TxtGlobalDesc02_TextChanged(object sender, TextChangedEventArgs e)
        {
            string rs = txtGlobalDesc02.Text.Trim();
            if (txtS3_Desc02 != null) txtS3_Desc02.Text = rs;
            if (txtS4_Desc02 != null) txtS4_Desc02.Text = rs;
            if (txtS5_Desc02 != null) txtS5_Desc02.Text = rs;
            if (txtS7_Desc02 != null) txtS7_Desc02.Text = rs;
        }

        // ─────────────────────────────────────────────────────────────────
        #region Step Indicator Dots & Overall Progress
        // ─────────────────────────────────────────────────────────────────

        private void BuildStepIndicators()
        {
            _stepDots = new Border[TotalSteps + 1];

            for (int i = 1; i <= TotalSteps; i++)
            {
                var label = new TextBlock
                {
                    Text                = i.ToString(),
                    Foreground          = MediaBrushes.White,
                    FontWeight          = FontWeights.Bold,
                    FontSize            = 12,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment   = System.Windows.VerticalAlignment.Center,
                };

                var dot = new Border
                {
                    Width        = 32,
                    Height       = 32,
                    CornerRadius = new CornerRadius(16),
                    Background   = i == 1
                                   ? new SolidBrush(MediaColor.FromRgb(59, 130, 246))
                                   : new SolidBrush(MediaColor.FromRgb(51, 65, 85)),
                    Margin       = new Thickness(4, 0, 4, 0),
                    Child        = label,
                    ToolTip      = GetStepName(i)
                };

                _stepDots[i] = dot;
                stepIndicatorPanel.Children.Add(dot);

                if (i < TotalSteps)
                {
                    stepIndicatorPanel.Children.Add(new Border
                    {
                        Width             = 24,
                        Height            = 2,
                        Background        = new SolidBrush(MediaColor.FromRgb(51, 65, 85)),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    });
                }
            }
        }

        private string GetStepName(int step) => step switch
        {
            1 => "Step 1: MTO_AREAINF (Optional)",
            2 => "Step 2: MANAGE_MISSMATCH (Optional)",
            3 => "Step 3: MTO_LINFO00 (★ Required)",
            4 => "Step 4: MTO_LINFO01 (★ Required)",
            5 => "Step 5: MTO_LINFO02 (★ Required)",
            6 => "Step 6: UPDTMAPINF01 (Optional)",
            7 => "Step 7: MTO_PLOTINF01 (Optional)",
            _ => $"Step {step}"
        };

        private void RefreshStepDots()
        {
            int completedCount = 0;

            for (int i = 1; i <= TotalSteps; i++)
            {
                if (_stepDots[i] == null) continue;

                if (_stepCompleted[i])
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                    completedCount++;
                }
                else if (_stepSkipped[i])
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(217, 119, 6));
                    completedCount++;
                }
                else if (i == _currentStep)
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(59, 130, 246));
                }
                else
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(51, 65, 85));
                }
            }

            // Update Overall Progress Bar
            pbWizardOverall.Value = completedCount;
            int pct = (int)Math.Round((double)completedCount / TotalSteps * 100);
            lblWizardOverallPct.Text = $"{pct}% complete ({completedCount}/{TotalSteps} steps)";
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Navigation
        // ─────────────────────────────────────────────────────────────────

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                if (_stepCompleted[_currentStep - 1])
                {
                    MessageBox.Show(
                        "Failsafe Active: The previous step has already been completed and committed.\n\nGoing back to completed steps is disabled to prevent duplicate operations or data conflicts.",
                        "Step Locked",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                GoToStep(_currentStep - 1);
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_stepRequired[_currentStep] && !_stepCompleted[_currentStep])
            {
                MessageBox.Show(
                    $"Step {_currentStep} is a REQUIRED step.\n\nYou must successfully execute this step before proceeding to the next step.",
                    "Required Step Incomplete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_currentStep < TotalSteps)
            {
                GoToStep(_currentStep + 1);
            }
        }

        private void BtnSkipCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (_stepRequired[_currentStep])
            {
                MessageBox.Show(
                    $"Step {_currentStep} ({GetStepName(_currentStep)}) is a REQUIRED step and cannot be skipped.",
                    "Cannot Skip Required Step",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _stepSkipped[_currentStep] = true;
            RefreshStepDots();

            if (_currentStep < TotalSteps)
            {
                GoToStep(_currentStep + 1);
            }
            else
            {
                CheckCompletionBanner();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void GoToStep(int step)
        {
            _stepPanels[_currentStep].Visibility = Visibility.Collapsed;
            _currentStep = step;
            _stepPanels[_currentStep].Visibility = Visibility.Visible;
            RefreshNav();
        }

        private void RefreshNav()
        {
            txtStepCounter.Text = $"Step {_currentStep} of {TotalSteps}";
            // Failsafe: only allow Previous if previous step is NOT completed
            btnPrev.IsEnabled   = _currentStep > 1 && !_stepCompleted[_currentStep - 1];
            btnNext.IsEnabled   = _currentStep < TotalSteps;

            bool isRequired = _stepRequired[_currentStep];
            btnSkip.Visibility = isRequired ? Visibility.Collapsed : Visibility.Visible;
            btnSkip.IsEnabled  = !_stepCompleted[_currentStep];

            RefreshStepDots();
            CheckCompletionBanner();
        }

        private bool _completionPopupShown = false;

        private void CheckCompletionBanner()
        {
            if (IsMigrationCompleted)
            {
                if (doneBanner != null)
                    doneBanner.Visibility = Visibility.Visible;

                MigrationCompleted?.Invoke();

                if (!_completionPopupShown)
                {
                    _completionPopupShown = true;
                    OpenMigratedUnitsDialog();
                }
            }
        }

        private void BtnViewMigratedUnits_Click(object sender, RoutedEventArgs e)
        {
            OpenMigratedUnitsDialog();
        }

        private void OpenMigratedUnitsDialog()
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(_targetConnectionString)
                {
                    InitialCatalog = _sourceDbName
                };
                var dialog = new MigratedUnitsSummaryDialog(_sourceDbName, builder.ConnectionString, _targetConnectionString)
                {
                    Owner = this
                };
                bool? res = dialog.ShowDialog();
                if (res == true)
                {
                    // User clicked "OK — Continue" in the summary dialog to return to the main window
                    // and continue with other conversions or migrations
                    Close();
                }
            }
            catch (Exception ex)
            {
                var res = MessageBox.Show(
                    $"🎉 All Migration Steps Completed Successfully!\n\nAll data migration steps have been executed and saved into [DLRSLSINF_4103PRSDB].\n\n(Details: {ex.Message})\n\nClick OK to return to the main window.",
                    "Data Migration Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                if (res == MessageBoxResult.OK)
                {
                    Close();
                }
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Step Execution Helpers with Live Timer & Progress
        // ─────────────────────────────────────────────────────────────────

        private async Task ExecuteStepAsync(
            int stepNumber,
            string procId,
            string desc02,
            Button actionButton,
            TextBlock statusLabel,
            StackPanel runningPanel,
            TextBlock timerLabel,
            DataGrid? resultGrid = null)
        {
            if (string.IsNullOrWhiteSpace(_sourceDbName))
            {
                statusLabel.Text = "⚠ Source database name is missing.";
                statusLabel.Foreground = MediaBrushes.Tomato;
                statusLabel.Visibility = Visibility.Visible;
                return;
            }

            actionButton.IsEnabled = false;
            statusLabel.Visibility = Visibility.Collapsed;
            runningPanel.Visibility = Visibility.Visible;
            timerLabel.Text = $"⏳ Executing {procId} on SQL Server... Elapsed: 00:00";

            if (resultGrid != null) resultGrid.Visibility = Visibility.Collapsed;

            var sw = Stopwatch.StartNew();

            // Live Timer Dispatcher
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) =>
            {
                timerLabel.Text = $"⏳ Executing {procId} on SQL Server... Elapsed: {sw.Elapsed:mm\\:ss}";
            };
            timer.Start();

            try
            {
                var ds = await Task.Run(() =>
                {
                    using var conn = new SqlConnection(_targetConnectionString);
                    conn.Open();

                    using var cmd = new SqlCommand("dbo.SP_LIS_DATA_MIGRATION", conn)
                    {
                        CommandType    = CommandType.StoredProcedure,
                        CommandTimeout = 900 // 15 mins for large dataset migrations
                    };

                    cmd.Parameters.Add(new SqlParameter("@ComCod", SqlDbType.NVarChar, 2500) { Value = "4103" });
                    cmd.Parameters.Add(new SqlParameter("@ProcID", SqlDbType.NVarChar, 2500) { Value = procId });
                    cmd.Parameters.Add(new SqlParameter("@Desc01", SqlDbType.NVarChar, 2500) { Value = _sourceDbName });

                    if (!string.IsNullOrEmpty(desc02))
                    {
                        cmd.Parameters.Add(new SqlParameter("@Desc02", SqlDbType.NVarChar, 2500) { Value = desc02 });
                    }

                    var dataSet = new DataSet();
                    using var adapter = new SqlDataAdapter(cmd);
                    adapter.Fill(dataSet);
                    return dataSet;
                });

                timer.Stop();
                sw.Stop();
                runningPanel.Visibility = Visibility.Collapsed;

                double elapsedSec = Math.Round(sw.Elapsed.TotalSeconds, 1);
                string timeStr = sw.Elapsed.TotalSeconds < 60 ? $"{elapsedSec}s" : $"{sw.Elapsed:mm\\:ss}";

                // Check if result returned an error table (from internal TRY/CATCH in SP)
                if (ds.Tables.Count > 0 && ds.Tables[0].Columns.Contains("ErrMsg") && ds.Tables[0].Rows.Count > 0)
                {
                    var row = ds.Tables[0].Rows[0];
                    string errMsg = row["ErrMsg"]?.ToString() ?? "Unknown SQL error";
                    string errNo  = ds.Tables[0].Columns.Contains("ErrNo") ? row["ErrNo"]?.ToString() ?? "" : "";
                    statusLabel.Text = $"⚠ Procedure Error #{errNo}: {errMsg} (Elapsed: {timeStr})";
                    statusLabel.Foreground = MediaBrushes.Tomato;
                    statusLabel.Visibility = Visibility.Visible;
                    return;
                }

                // Step 2 specific handling for mismatch grid
                if (procId == "MANAGE_MISSMATCH" && resultGrid != null && ds.Tables.Count > 0)
                {
                    var table = ds.Tables[0];
                    if (table.Rows.Count > 0)
                    {
                        resultGrid.ItemsSource = table.DefaultView;
                        resultGrid.Visibility  = Visibility.Visible;
                        statusLabel.Text = $"⚠ Identified {table.Rows.Count} mismatched Khatian record(s). Details shown below. (Completed in {timeStr})";
                        statusLabel.Foreground = new SolidBrush(MediaColor.FromRgb(251, 191, 36));
                    }
                    else
                    {
                        statusLabel.Text = $"✔ No mismatched Khatians found! Source tables are consistent. (Completed in {timeStr})";
                        statusLabel.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                    }
                }
                else
                {
                    statusLabel.Text = $"✔ Step {stepNumber} ({procId}) completed successfully in {timeStr}.";
                    statusLabel.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                }

                statusLabel.Visibility = Visibility.Visible;
                _stepCompleted[stepNumber] = true;
                _stepSkipped[stepNumber]   = false;
                actionButton.IsEnabled     = false;
                actionButton.Content       = "✔ Completed (Locked)";
                RefreshStepDots();
                CheckCompletionBanner();
                RefreshNav();
            }
            catch (SqlException sqlEx)
            {
                timer.Stop();
                sw.Stop();
                runningPanel.Visibility = Visibility.Collapsed;
                statusLabel.Text = $"⚠ SQL Error {sqlEx.Number}: {sqlEx.Message} (Elapsed: {sw.Elapsed:mm\\:ss})";
                statusLabel.Foreground = MediaBrushes.Tomato;
                statusLabel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                timer.Stop();
                sw.Stop();
                runningPanel.Visibility = Visibility.Collapsed;
                statusLabel.Text = $"⚠ Error: {ex.Message}";
                statusLabel.Foreground = MediaBrushes.Tomato;
                statusLabel.Visibility = Visibility.Visible;
            }
            finally
            {
                actionButton.IsEnabled = !_stepCompleted[stepNumber];
                if (_stepCompleted[stepNumber])
                {
                    actionButton.Content = "✔ Completed (Locked)";
                }
                RefreshNav();
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Step Action Handlers
        // ─────────────────────────────────────────────────────────────────

        // Step 1: MTO_AREAINF
        private async void BtnRunS1_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteStepAsync(1, "MTO_AREAINF", "", btnRunS1, lblS1Status, panelRunningS1, lblTimerS1);
        }

        // Step 2: MANAGE_MISSMATCH
        private async void BtnRunS2_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteStepAsync(2, "MANAGE_MISSMATCH", "", btnRunS2, lblS2Status, panelRunningS2, lblTimerS2, dgMismatch);
        }

        // Step 3: MTO_LINFO00 (Required)
        private async void BtnRunS3_Click(object sender, RoutedEventArgs e)
        {
            string rs = txtS3_Desc02?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(rs))
            {
                rs = txtGlobalDesc02?.Text?.Trim() ?? "201901";
                if (string.IsNullOrEmpty(rs)) rs = "201901";
                if (txtS3_Desc02 != null) txtS3_Desc02.Text = rs;
            }
            await ExecuteStepAsync(3, "MTO_LINFO00", rs, btnRunS3, lblS3Status, panelRunningS3, lblTimerS3);
        }

        // Step 4: MTO_LINFO01 (Required)
        private async void BtnRunS4_Click(object sender, RoutedEventArgs e)
        {
            string rs = txtS4_Desc02?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(rs))
            {
                rs = txtGlobalDesc02?.Text?.Trim() ?? "201901";
                if (string.IsNullOrEmpty(rs)) rs = "201901";
                if (txtS4_Desc02 != null) txtS4_Desc02.Text = rs;
            }
            await ExecuteStepAsync(4, "MTO_LINFO01", rs, btnRunS4, lblS4Status, panelRunningS4, lblTimerS4);
        }

        // Step 5: MTO_LINFO02 (Required)
        private async void BtnRunS5_Click(object sender, RoutedEventArgs e)
        {
            string rs = txtS5_Desc02?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(rs))
            {
                rs = txtGlobalDesc02?.Text?.Trim() ?? "201901";
                if (string.IsNullOrEmpty(rs)) rs = "201901";
                if (txtS5_Desc02 != null) txtS5_Desc02.Text = rs;
            }
            await ExecuteStepAsync(5, "MTO_LINFO02", rs, btnRunS5, lblS5Status, panelRunningS5, lblTimerS5);
        }

        // Step 6: UPDTMAPINF01
        private async void BtnRunS6_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteStepAsync(6, "UPDTMAPINF01", "", btnRunS6, lblS6Status, panelRunningS6, lblTimerS6);
        }

        // Step 7: MTO_PLOTINF01
        private async void BtnRunS7_Click(object sender, RoutedEventArgs e)
        {
            string rs = txtS7_Desc02?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(rs))
            {
                rs = txtGlobalDesc02?.Text?.Trim() ?? "201901";
                if (string.IsNullOrEmpty(rs)) rs = "201901";
                if (txtS7_Desc02 != null) txtS7_Desc02.Text = rs;
            }
            await ExecuteStepAsync(7, "MTO_PLOTINF01", rs, btnRunS7, lblS7Status, panelRunningS7, lblTimerS7);
        }

        #endregion
    }
}
