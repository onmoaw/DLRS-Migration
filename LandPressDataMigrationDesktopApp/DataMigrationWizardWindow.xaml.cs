using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using SolidBrush = System.Windows.Media.SolidColorBrush;
using Border = System.Windows.Controls.Border;
using TextBlock = System.Windows.Controls.TextBlock;
using TextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Sequential Data Migration Pipeline Wizard:
    /// Executes stored procedures in the sequence configured centrally in AppConfig.GetPipelineSteps().
    /// Runs all steps one by one automatically by default, with top-right control to switch to manual step-by-step.
    /// Includes intelligent failsafes for commented-out SP branches in SQL Server.
    /// </summary>
    public partial class DataMigrationWizardWindow : Window
    {
        private readonly string _targetConnectionString;
        private readonly string _sourceDbName;

        public class StepState
        {
            public MigrationStepConfig Config { get; set; } = null!;
            public bool IsCompleted { get; set; }
            public bool IsSkipped { get; set; }
            public bool IsBypassedDueToDb { get; set; }
            public string? StatusText { get; set; }
            public bool IsStatusError { get; set; }
            public bool IsStatusWarning { get; set; }
            public DataTable? ResultTable { get; set; }
            public string CustomRsNumber { get; set; } = "";
        }

        private readonly List<StepState> _steps = new();
        private int _currentStepIndex = 1; // 1-based index

        private Border[] _stepDots = Array.Empty<Border>();
        private bool _isExecutingStep = false;
        private bool _isAutoRunning = false;
        private bool _isAutoPaused = false;
        private bool _completionPopupShown = false;

        private readonly Stopwatch _overallStopwatch = new();
        private DispatcherTimer? _overallTimer;

        /// <summary>
        /// Fires immediately when all required migration steps in the sequential pipeline complete.
        /// </summary>
        public event Action? MigrationCompleted;

        /// <summary>
        /// Returns true if all core required migration steps were completed.
        /// </summary>
        public bool IsMigrationCompleted { get; private set; }
        private readonly bool _autoStartOnLoad = false;

        /// <summary>
        /// Returns a formatted summary of all executed, bypassed, and skipped ProcIDs.
        /// </summary>
        public string GetExecutionSummary()
        {
            var executed = _steps.Where(s => s.IsCompleted && !s.IsBypassedDueToDb).Select(s => s.Config.ProcId).ToList();
            var bypassed = _steps.Where(s => s.IsBypassedDueToDb).Select(s => s.Config.ProcId).ToList();
            var skipped  = _steps.Where(s => s.IsSkipped).Select(s => s.Config.ProcId).ToList();

            var parts = new List<string>();
            if (executed.Count > 0) parts.Add($"Executed: {string.Join(",", executed)}");
            if (bypassed.Count > 0) parts.Add($"Bypassed: {string.Join(",", bypassed)}");
            if (skipped.Count > 0)  parts.Add($"Skipped: {string.Join(",", skipped)}");

            string summary = string.Join(" | ", parts);
            return summary.Length > 490 ? summary.Substring(0, 490) : summary;
        }

        public DataMigrationWizardWindow(string targetConnectionString, string sourceDbName, bool autoStart = false)
        {
            InitializeComponent();

            _targetConnectionString = targetConnectionString;
            _sourceDbName = string.IsNullOrWhiteSpace(sourceDbName) ? AppConfig.IntermediateDbName : sourceDbName;
            _autoStartOnLoad = autoStart;

            txtSourceDbDisplay.Text = _sourceDbName;
            txtParamDesc01.Text = _sourceDbName;

            LoadPipelineFromConfig();
            BuildStepIndicators();
            RenderActiveStep();
            ApplyModeView();

            UpdateThemeButton();
            ApplyRoleView();

            ThemeManager.ThemeChanged += OnThemeChanged;
            Closing += DataMigrationWizardWindow_Closing;
            Closed += (s, e) =>
            {
                ThemeManager.ThemeChanged -= OnThemeChanged;
                _overallTimer?.Stop();
            };

            if (_autoStartOnLoad)
            {
                Loaded += async (s, e) =>
                {
                    await Task.Delay(400); // brief pause to render UI
                    if (!_isAutoRunning)
                    {
                        BtnStartAuto_Click(this, new RoutedEventArgs());
                    }
                };
            }
        }

        #region Initialization from Central AppConfig

        private void LoadPipelineFromConfig()
        {
            _steps.Clear();
            var configuredSteps = AppConfig.GetPipelineSteps()
                .Where(s => s.IsEnabled)
                .OrderBy(s => s.Order)
                .ToList();

            for (int i = 0; i < configuredSteps.Count; i++)
            {
                var cfg = configuredSteps[i];
                _steps.Add(new StepState
                {
                    Config = cfg,
                    CustomRsNumber = txtGlobalDesc02?.Text?.Trim() ?? "201901"
                });
            }

            if (_steps.Count == 0)
            {
                MessageBox.Show("No migration steps are currently enabled in AppConfig.PipelineSteps.", "Pipeline Configuration Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private StepState CurrentStep => _steps[_currentStepIndex - 1];

        #endregion

        #region Execution Mode Switcher (Auto vs Manual)

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            ApplyModeView();
        }

        private void ApplyModeView()
        {
            if (rbModeAuto == null || panelAutoRunControl == null) return;

            bool isAuto = rbModeAuto.IsChecked == true;

            panelAutoRunControl.Visibility = isAuto ? Visibility.Visible : Visibility.Collapsed;
            panelManualActions.Visibility  = isAuto ? Visibility.Collapsed : Visibility.Visible;

            // In Auto mode, hide manual step buttons; in Manual mode, show them
            btnPrev.Visibility = isAuto ? Visibility.Collapsed : Visibility.Visible;
            btnNext.Visibility = isAuto ? Visibility.Collapsed : Visibility.Visible;
            btnSkip.Visibility = isAuto ? Visibility.Collapsed : Visibility.Visible;

            if (isAuto)
            {
                lblAutoStatus.Text = $"Ready to execute {_steps.Count} steps sequentially.";
            }
            else
            {
                RefreshNav();
            }
        }

        #endregion

        #region Step Indicators & UI Rendering

        private void BuildStepIndicators()
        {
            stepIndicatorPanel.Children.Clear();
            int total = _steps.Count;
            _stepDots = new Border[total + 1];

            for (int i = 1; i <= total; i++)
            {
                int stepNum = i;
                var step = _steps[i - 1];

                var label = new TextBlock
                {
                    Text = i.ToString(),
                    Foreground = MediaBrushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment   = System.Windows.VerticalAlignment.Center,
                };

                var dot = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(18),
                    Background = i == _currentStepIndex
                        ? new SolidBrush(MediaColor.FromRgb(59, 130, 246))
                        : new SolidBrush(MediaColor.FromRgb(51, 65, 85)),
                    Margin = new Thickness(5, 0, 5, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = label,
                    ToolTip = $"{step.Config.GetTitle(UserSession.IsAdmin)} (Click to View)"
                };

                dot.MouseLeftButtonUp += (s, e) =>
                {
                    if (_isExecutingStep || _isAutoRunning) return;
                    GoToStep(stepNum);
                };

                _stepDots[i] = dot;
                stepIndicatorPanel.Children.Add(dot);

                if (i < total)
                {
                    stepIndicatorPanel.Children.Add(new Border
                    {
                        Width = 20,
                        Height = 2,
                        Background = new SolidBrush(MediaColor.FromRgb(51, 65, 85)),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    });
                }
            }

            pbWizardOverall.Maximum = total;
            RefreshStepDots();
        }

        private void RefreshStepDots()
        {
            int total = _steps.Count;
            int completedCount = 0;

            for (int i = 1; i <= total; i++)
            {
                if (i >= _stepDots.Length || _stepDots[i] == null) continue;

                var step = _steps[i - 1];
                if (step.IsCompleted)
                {
                    if (step.IsBypassedDueToDb)
                    {
                        _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(217, 119, 6)); // Amber for bypassed
                    }
                    else
                    {
                        _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129)); // Green for completed
                    }
                    completedCount++;
                }
                else if (step.IsSkipped)
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(100, 116, 139));
                    completedCount++;
                }
                else if (i == _currentStepIndex)
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(59, 130, 246));
                }
                else
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(51, 65, 85));
                }
            }

            pbWizardOverall.Value = completedCount;
            int pct = total == 0 ? 0 : (int)Math.Round((double)completedCount / total * 100);
            lblWizardOverallPct.Text = $"{pct}% complete ({completedCount}/{total} steps)";
        }

        private void RenderActiveStep()
        {
            if (_steps.Count == 0) return;
            var step = CurrentStep;
            var cfg = step.Config;
            bool isAdmin = UserSession.IsAdmin;

            txtStepTitle.Text = cfg.GetTitle(isAdmin);
            txtStepDesc.Text = cfg.GetDescription(isAdmin);

            if (cfg.IsRequired)
            {
                txtStepType.Text = "★ Required (Must Complete)";
                bdStepType.Background = new SolidBrush(MediaColor.FromRgb(136, 19, 55));
                txtStepType.Foreground = new SolidBrush(MediaColor.FromRgb(253, 164, 175));
            }
            else
            {
                txtStepType.Text = "Optional — Can Skip";
                bdStepType.Background = new SolidBrush(MediaColor.FromRgb(22, 78, 99));
                txtStepType.Foreground = new SolidBrush(MediaColor.FromRgb(103, 232, 249));
            }

            if (borderStepProcBadge != null)
            {
                borderStepProcBadge.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
                txtStepProcBadge.Text = $"{cfg.SpName} · @ProcID='{cfg.ProcId}'";
            }

            if (txtParamTargetProc != null) txtParamTargetProc.Text = cfg.SpName;
            if (txtParamProcId != null) txtParamProcId.Text = cfg.ProcId;
            if (txtParamDesc01 != null) txtParamDesc01.Text = _sourceDbName;

            if (rowParamTargetProc != null) rowParamTargetProc.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            if (rowParamProcId != null) rowParamProcId.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            if (rowParamDesc01 != null) rowParamDesc01.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

            if (borderParametersPanel != null)
            {
                // Only show parameters panel for standard users if this step requires input (e.g. RS number)
                borderParametersPanel.Visibility = (isAdmin || cfg.RequiresRsNumber) ? Visibility.Visible : Visibility.Collapsed;
                if (txtExecutionParamsHeader != null)
                {
                    txtExecutionParamsHeader.Text = isAdmin ? "Execution Parameters:" : "Step Configuration:";
                }
            }

            // RS Number panel
            if (cfg.RequiresRsNumber)
            {
                panelParamRs.Visibility = Visibility.Visible;
                txtStepRsNumber.Text = !string.IsNullOrEmpty(step.CustomRsNumber)
                    ? step.CustomRsNumber
                    : (txtGlobalDesc02?.Text?.Trim() ?? "201901");
                if (lblParamRs != null)
                {
                    lblParamRs.Text = isAdmin ? "RS Number (@Desc02):" : "RS Number:";
                }
            }
            else
            {
                panelParamRs.Visibility = Visibility.Collapsed;
            }

            // Action button state
            if (step.IsCompleted)
            {
                btnExecuteStep.Content = step.IsBypassedDueToDb ? "✔ Bypassed (In DB)" : "✔ Completed (Locked)";
                btnExecuteStep.IsEnabled = false;
                btnExecuteStep.Background = new SolidBrush(MediaColor.FromRgb(30, 41, 59));
            }
            else
            {
                btnExecuteStep.Content = isAdmin ? $"▶ Execute {cfg.ProcId}" : $"▶ Run Step {_currentStepIndex}";
                btnExecuteStep.IsEnabled = true;
                btnExecuteStep.Background = (System.Windows.Media.Brush)FindResource("GreenBrush");
            }

            // Skip button
            btnSkipCurrentStep.Visibility = (cfg.IsRequired || step.IsCompleted)
                ? Visibility.Collapsed
                : Visibility.Visible;

            // Status message
            if (!string.IsNullOrEmpty(step.StatusText))
            {
                bdStatusAlert.Visibility = Visibility.Visible;
                lblStatus.Text = step.StatusText;
                if (step.IsStatusError)
                {
                    lblStatus.Foreground = MediaBrushes.Tomato;
                    bdStatusAlert.BorderBrush = MediaBrushes.Tomato;
                }
                else if (step.IsStatusWarning)
                {
                    lblStatus.Foreground = new SolidBrush(MediaColor.FromRgb(251, 191, 36));
                    bdStatusAlert.BorderBrush = new SolidBrush(MediaColor.FromRgb(251, 191, 36));
                }
                else
                {
                    lblStatus.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                    bdStatusAlert.BorderBrush = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                }
            }
            else
            {
                bdStatusAlert.Visibility = Visibility.Collapsed;
            }

            // Result DataGrid
            if (step.ResultTable != null && step.ResultTable.Rows.Count > 0)
            {
                panelResultsGrid.Visibility = Visibility.Visible;
                dgResults.ItemsSource = step.ResultTable.DefaultView;
                lblResultCount.Text = $"{step.ResultTable.Rows.Count:N0} record(s) returned";
            }
            else
            {
                panelResultsGrid.Visibility = Visibility.Collapsed;
                dgResults.ItemsSource = null;
            }
        }

        private void GoToStep(int stepNumber)
        {
            SaveCurrentStepInputs();
            _currentStepIndex = stepNumber;
            RenderActiveStep();
            RefreshNav();
        }

        private void SaveCurrentStepInputs()
        {
            if (_steps.Count == 0) return;
            var step = CurrentStep;
            if (step.Config.RequiresRsNumber && txtStepRsNumber != null)
            {
                step.CustomRsNumber = txtStepRsNumber.Text.Trim();
            }
        }

        private void RefreshNav()
        {
            int total = _steps.Count;
            txtStepCounter.Text = $"Step {_currentStepIndex} of {total}";

            bool isManual = rbModeManual?.IsChecked == true;
            if (isManual)
            {
                btnPrev.IsEnabled = _currentStepIndex > 1 && !_steps[_currentStepIndex - 2].IsCompleted;
                btnNext.IsEnabled = _currentStepIndex < total;

                bool isRequired = CurrentStep.Config.IsRequired;
                btnSkip.Visibility = isRequired ? Visibility.Collapsed : Visibility.Visible;
                btnSkip.IsEnabled  = !CurrentStep.IsCompleted;
            }

            RefreshStepDots();
            CheckCompletionBanner();
        }

        #endregion

        #region Auto-Run Sequence Engine

        private async void BtnStartAuto_Click(object sender, RoutedEventArgs e)
        {
            if (_isAutoRunning) return;

            _isAutoRunning = true;
            _isAutoPaused  = false;
            btnStartAuto.Visibility = Visibility.Collapsed;
            btnPauseAuto.Visibility = Visibility.Visible;
            btnStopAuto.Visibility  = Visibility.Visible;

            if (!_overallStopwatch.IsRunning)
            {
                _overallStopwatch.Restart();
                _overallTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _overallTimer.Tick += (s, ev) =>
                {
                    lblOverallTimer.Text = $"Total Elapsed: {_overallStopwatch.Elapsed:mm\\:ss}";
                };
                _overallTimer.Start();
            }

            try
            {
                for (int i = 1; i <= _steps.Count; i++)
                {
                    if (!_isAutoRunning) break;

                    while (_isAutoPaused)
                    {
                        lblAutoStatus.Text = "⏸ Auto-Run Paused. Click 'Resume Auto-Run' to continue.";
                        await Task.Delay(500);
                        if (!_isAutoRunning) break;
                    }
                    if (!_isAutoRunning) break;

                    var step = _steps[i - 1];
                    GoToStep(i);

                    // Skip steps already completed
                    if (step.IsCompleted) continue;

                    lblAutoStatus.Text = UserSession.IsAdmin
                        ? $"⚡ Executing Step {i} of {_steps.Count}: {step.Config.ProcId}..."
                        : $"⚡ Executing Step {i} of {_steps.Count}: {step.Config.GetTitle(false)}...";

                    bool success = await ExecuteStepInternalAsync(step);
                    if (!success)
                    {
                        // Genuine SQL error occurred and was not a commented-out branch
                        lblAutoStatus.Text = UserSession.IsAdmin
                            ? $"⚠ Error in Step {i} ({step.Config.ProcId}). Auto-run paused."
                            : $"⚠ Error in Step {i}. Auto-run paused.";

                        string stepName = UserSession.IsAdmin
                            ? $"Step {i} ({step.Config.Title})"
                            : $"Step {i} ({step.Config.GetTitle(false)})";

                        var choice = MessageBox.Show(
                            $"{stepName} encountered an error:\n\n{step.StatusText}\n\nDo you want to skip this step and continue the rest of the migration sequence?",
                            "Migration Step Alert",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (choice == MessageBoxResult.Yes)
                        {
                            step.IsSkipped = true;
                            RefreshStepDots();
                            continue;
                        }
                        else
                        {
                            _isAutoRunning = false;
                            break;
                        }
                    }

                    await Task.Delay(AppConfig.AutoRunStepDelayMs);
                }

                if (_isAutoRunning)
                {
                    lblAutoStatus.Text = "✔ All sequential migration steps completed!";
                    CheckCompletionBanner();
                }
            }
            finally
            {
                _isAutoRunning = false;
                _isAutoPaused = false;
                btnStartAuto.Visibility = Visibility.Visible;
                btnPauseAuto.Visibility = Visibility.Collapsed;
                btnStopAuto.Visibility  = Visibility.Collapsed;
                RefreshNav();
            }
        }

        private void BtnPauseAuto_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAutoPaused)
            {
                _isAutoPaused = true;
                btnPauseAuto.Content = "▶ Resume Auto-Run";
            }
            else
            {
                _isAutoPaused = false;
                btnPauseAuto.Content = "⏸ Pause Auto-Run";
            }
        }

        private void BtnStopAuto_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Are you sure you want to stop the automated migration sequence?\n\nCurrently active procedure execution will complete, but subsequent migration steps will be halted.",
                "Confirm Stop Auto-Run",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            _isAutoRunning = false;
            _isAutoPaused  = false;
            lblAutoStatus.Text = "⏹ Auto-Run Stopped by user.";
            btnStartAuto.Visibility = Visibility.Visible;
            btnPauseAuto.Visibility = Visibility.Collapsed;
            btnStopAuto.Visibility  = Visibility.Collapsed;
        }

        #endregion

        #region Step Execution with Commented-Out DB Failsafe

        private async void BtnExecuteStep_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteCurrentStepAsync();
        }

        private async Task ExecuteCurrentStepAsync()
        {
            var step = CurrentStep;
            SaveCurrentStepInputs();
            await ExecuteStepInternalAsync(step);
            RefreshNav();
        }

        /// <summary>
        /// Executes a single step against SQL Server.
        /// Catches and safely bypasses steps where the SP or ProcID branch was commented out in the database.
        /// </summary>
        private async Task<bool> ExecuteStepInternalAsync(StepState step)
        {
            if (string.IsNullOrWhiteSpace(_sourceDbName))
            {
                MessageBox.Show("Source staging database name is missing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            _isExecutingStep = true;
            btnExecuteStep.IsEnabled = false;
            panelRunning.Visibility = Visibility.Visible;
            bdStatusAlert.Visibility = Visibility.Collapsed;
            panelResultsGrid.Visibility = Visibility.Collapsed;

            var cfg = step.Config;
            string procId = cfg.ProcId;
            string spName = cfg.SpName;
            bool isAdmin = UserSession.IsAdmin;
            lblTimer.Text = isAdmin
                ? $"⏳ Executing {procId} on SQL Server... Elapsed: 00:00"
                : $"⏳ Executing Step {_currentStepIndex}... Elapsed: 00:00";

            var sw = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) =>
            {
                lblTimer.Text = isAdmin
                    ? $"⏳ Executing {procId} on SQL Server... Elapsed: {sw.Elapsed:mm\\:ss}"
                    : $"⏳ Executing Step {_currentStepIndex}... Elapsed: {sw.Elapsed:mm\\:ss}";
            };
            timer.Start();

            PipelineExecutionLogger.LogInfo("Step 3 (Migration SP)", $"Executing {procId} ({cfg.Title}) on {spName}...", recordId: procId);

            try
            {
                string rsNum = cfg.RequiresRsNumber
                    ? (!string.IsNullOrEmpty(step.CustomRsNumber) ? step.CustomRsNumber : (txtGlobalDesc02?.Text?.Trim() ?? "201901"))
                    : "";

                var ds = await Task.Run(() =>
                {
                    using var conn = new SqlConnection(_targetConnectionString);
                    conn.Open();

                    using var cmd = new SqlCommand(spName, conn)
                    {
                        CommandType = CommandType.StoredProcedure,
                        CommandTimeout = 900 // 15 mins for large data batches
                    };

                    cmd.Parameters.Add(new SqlParameter("@ComCod", SqlDbType.NVarChar, 2500) { Value = "4103" });
                    cmd.Parameters.Add(new SqlParameter("@ProcID", SqlDbType.NVarChar, 2500) { Value = procId });
                    cmd.Parameters.Add(new SqlParameter("@Desc01", SqlDbType.NVarChar, 2500) { Value = _sourceDbName });

                    if (cfg.RequiresRsNumber && !string.IsNullOrEmpty(rsNum))
                    {
                        cmd.Parameters.Add(new SqlParameter("@Desc02", SqlDbType.NVarChar, 2500) { Value = rsNum });
                    }

                    var dataSet = new DataSet();
                    using var adapter = new SqlDataAdapter(cmd);
                    adapter.Fill(dataSet);
                    return dataSet;
                });

                timer.Stop();
                sw.Stop();
                panelRunning.Visibility = Visibility.Collapsed;

                double elapsedSec = Math.Round(sw.Elapsed.TotalSeconds, 1);
                string timeStr = sw.Elapsed.TotalSeconds < 60 ? $"{elapsedSec}s" : $"{sw.Elapsed:mm\\:ss}";

                // Check internal SQL TRY/CATCH error table
                if (ds.Tables.Count > 0 && ds.Tables[0].Columns.Contains("ErrMsg") && ds.Tables[0].Rows.Count > 0)
                {
                    var row = ds.Tables[0].Rows[0];
                    string errMsg = row["ErrMsg"]?.ToString() ?? "Unknown SQL error";

                    // =========================================================
                    // FAILSAFE: Check if the user commented out this branch in the DB
                    // =========================================================
                    if (IsCommentedOutOrNotFoundInDb(errMsg))
                    {
                        step.IsBypassedDueToDb = true;
                        step.IsCompleted = true;
                        step.IsSkipped = false;
                        step.IsStatusWarning = true;
                        step.IsStatusError = false;
                        step.StatusText = isAdmin
                            ? $"ℹ Notice: Bypassed — Branch '{procId}' was commented out or omitted in the database. Safely continued."
                            : $"ℹ Notice: Step {_currentStepIndex} was marked as bypassed in database setup. Safely continued.";
                        PipelineExecutionLogger.LogWarning("Step 3 (Migration SP)", $"Bypassed {procId} — branch commented out in database.", recordId: procId);
                        RenderActiveStep();
                        RefreshStepDots();
                        return true; // Successfully bypassed without breaking pipeline!
                    }

                    step.IsStatusError = true;
                    step.StatusText = isAdmin
                        ? $"⚠ Procedure Error: {errMsg} (Elapsed: {timeStr})"
                        : $"⚠ Step Error: {errMsg} (Elapsed: {timeStr})";
                    RenderActiveStep();
                    return false;
                }

                // Handle tables returned by the procedure
                if (ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    step.ResultTable = ds.Tables[0];
                    if (procId == "MANAGE_MISSMATCH")
                    {
                        step.IsStatusWarning = true;
                        step.StatusText = $"⚠ Identified {step.ResultTable.Rows.Count} mismatched Khatian record(s). Details shown below. (Completed in {timeStr})";
                        PipelineExecutionLogger.LogWarning("Step 3 (Migration SP)", $"Identified {step.ResultTable.Rows.Count} mismatched Khatian records.", recordId: procId);
                    }
                    else
                    {
                        step.IsStatusWarning = false;
                        step.StatusText = $"✔ Step completed successfully in {timeStr}. {step.ResultTable.Rows.Count:N0} records returned.";
                        PipelineExecutionLogger.LogInfo("Step 3 (Migration SP)", $"Completed {procId} in {timeStr}. {step.ResultTable.Rows.Count:N0} records returned.", recordId: procId);
                    }
                }
                else
                {
                    step.IsStatusWarning = false;
                    step.StatusText = isAdmin
                        ? $"✔ Step {step.Config.Order} ({procId}) completed successfully in {timeStr}."
                        : $"✔ Step {step.Config.Order} completed successfully in {timeStr}.";
                    PipelineExecutionLogger.LogInfo("Step 3 (Migration SP)", $"Step {step.Config.Order} ({procId}) completed successfully in {timeStr}.", recordId: procId);
                }

                step.IsStatusError = false;
                step.IsCompleted = true;
                step.IsSkipped = false;
                step.IsBypassedDueToDb = false;

                RenderActiveStep();
                RefreshStepDots();
                CheckCompletionBanner();
                return true;
            }
            catch (SqlException sqlEx)
            {
                timer.Stop();
                sw.Stop();
                panelRunning.Visibility = Visibility.Collapsed;

                // =========================================================
                // FAILSAFE: Check if SQL Server threw because ProcID was commented out
                // =========================================================
                if (IsCommentedOutOrNotFoundInDb(sqlEx.Message))
                {
                    step.IsBypassedDueToDb = true;
                    step.IsCompleted = true;
                    step.IsSkipped = false;
                    step.IsStatusWarning = true;
                    step.IsStatusError = false;
                    step.StatusText = isAdmin
                        ? $"ℹ Notice: Bypassed — Procedure or branch '{procId}' was commented out in the database. Safely continued."
                        : $"ℹ Notice: Step {_currentStepIndex} was marked as bypassed in database setup. Safely continued.";
                    PipelineExecutionLogger.LogWarning("Step 3 (Migration SP)", $"Bypassed {procId} — branch commented out in database.", recordId: procId);
                    RenderActiveStep();
                    RefreshStepDots();
                    return true; // Safely continued!
                }

                step.IsStatusError = true;
                step.StatusText = isAdmin
                    ? $"⚠ SQL Error {sqlEx.Number}: {sqlEx.Message} (Elapsed: {sw.Elapsed:mm\\:ss})"
                    : $"⚠ Execution Error: {sqlEx.Message} (Elapsed: {sw.Elapsed:mm\\:ss})";
                PipelineExecutionLogger.LogError("Step 3 (Migration SP)", $"SQL Error {sqlEx.Number} executing {procId}: {sqlEx.Message}", recordId: procId);
                RenderActiveStep();
                return false;
            }
            catch (Exception ex)
            {
                timer.Stop();
                sw.Stop();
                panelRunning.Visibility = Visibility.Collapsed;

                step.IsStatusError = true;
                step.StatusText = $"⚠ Error: {ex.Message}";
                RenderActiveStep();
                return false;
            }
            finally
            {
                _isExecutingStep = false;
            }
        }

        private bool IsCommentedOutOrNotFoundInDb(string message)
        {
            return AppConfig.IsCommentedOutOrNotFoundInDb(message);
        }

        #endregion

        #region Navigation & Completion

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStepIndex > 1)
            {
                if (_steps[_currentStepIndex - 2].IsCompleted)
                {
                    MessageBox.Show(
                        "Failsafe Active: The previous step has already been completed and committed.\n\nGoing back to completed steps is disabled to prevent duplicate operations or data conflicts.",
                        "Step Locked",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                GoToStep(_currentStepIndex - 1);
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentStep.Config.IsRequired && !CurrentStep.IsCompleted)
            {
                MessageBox.Show(
                    $"Step {_currentStepIndex} is a REQUIRED step.\n\nYou must successfully execute this step before proceeding to the next step.",
                    "Required Step Incomplete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_currentStepIndex < _steps.Count)
            {
                GoToStep(_currentStepIndex + 1);
            }
        }

        private void BtnSkipCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentStep.Config.IsRequired)
            {
                MessageBox.Show(
                    $"Step {_currentStepIndex} ({CurrentStep.Config.Title}) is a REQUIRED step and cannot be skipped.",
                    "Cannot Skip Required Step",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            CurrentStep.IsSkipped = true;
            PipelineExecutionLogger.LogInfo("Step 3 (Migration SP)", $"User skipped step {_currentStepIndex}: {CurrentStep.Config.ProcId} ({CurrentStep.Config.Title})", recordId: CurrentStep.Config.ProcId);
            RefreshStepDots();

            if (_currentStepIndex < _steps.Count)
            {
                GoToStep(_currentStepIndex + 1);
            }
            else
            {
                CheckCompletionBanner();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void CheckCompletionBanner()
        {
            var requiredSteps = _steps.Where(s => s.Config.IsRequired).ToList();
            bool allRequiredDone = requiredSteps.Count > 0 && requiredSteps.All(s => s.IsCompleted);

            if (allRequiredDone)
            {
                IsMigrationCompleted = true;
                string summary = GetExecutionSummary();
                PipelineExecutionLogger.LogInfo("Step 3 (Migration SP)", $"All required migration steps finished successfully! {summary}");
                if (doneBanner != null)
                    doneBanner.Visibility = Visibility.Visible;

                if (txtDoneBannerDetail != null)
                {
                    txtDoneBannerDetail.Text = UserSession.IsAdmin
                        ? "Core Khatians, Plots, and Owners data have been migrated and committed to [DLRSLSINF_4103PRSDB]."
                        : "Core Khatians, plots, and owners data have been successfully migrated and committed into the core system.";
                }

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
                    Close();
                }
            }
            catch (Exception ex)
            {
                var res = MessageBox.Show(
                    $"🎉 Sequential Migration Complete!\n\nAll required migration steps have been executed and saved into [DLRSLSINF_4103PRSDB].\n\n(Details: {ex.Message})\n\nClick OK to return to the main window.",
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

        #region Typography, Theme & Failsafes

        private void TxtGlobalDesc02_TextChanged(object sender, TextChangedEventArgs e)
        {
            string rs = txtGlobalDesc02.Text.Trim();
            if (txtStepRsNumber != null && CurrentStep.Config.RequiresRsNumber)
            {
                txtStepRsNumber.Text = rs;
                CurrentStep.CustomRsNumber = rs;
            }
        }

        private async void BtnDeploySp_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will verify that all stored procedures used in this pipeline exist in the target database [DLRSLSINF_4103PRSDB]:\n\n" +
                "• dbo.SP_LIS_DATA_MIGRATION\n" +
                "• dbo.SP_LIS_DATA_MIGRATION_01\n" +
                "• dbo.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK\n\n" +
                "Do you want to proceed with verification?",
                "Verify Stored Procedures",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            btnDeploySp.IsEnabled = false;
            btnDeploySp.Content = "⏳ Verifying...";

            try
            {
                var (allOk, summary) = await StoredProcedureDeployer.DeployAllProceduresAsync(_targetConnectionString);
                MessageBox.Show(
                    summary,
                    allOk ? "Stored Procedures Verified" : "Verification Report",
                    MessageBoxButton.OK,
                    allOk ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verification encountered an error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnDeploySp.IsEnabled = true;
                btnDeploySp.Content = "🛠 Verify Stored Procedures in DB";
            }
        }

        private void BtnViewRecords_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ViewRecordsWindow
                {
                    Owner = this
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Error opening View Records window:\n" + ex.Message,
                    "View Records Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e) => ThemeManager.ToggleTheme();

        private void OnThemeChanged(AppTheme theme)
        {
            UpdateThemeButton();
            RefreshStepDots();
            RenderActiveStep();
        }

        private void UpdateThemeButton()
        {
            if (txtThemeIcon == null || txtThemeLabel == null) return;
            bool isDark = ThemeManager.CurrentTheme == AppTheme.Dark;
            txtThemeIcon.Text = isDark ? "🌙" : "☀️";
            txtThemeLabel.Text = isDark ? "Dark" : "Light";
        }

        private void ApplyRoleView()
        {
            if (!UserSession.IsAdmin)
            {
                if (panelComCod != null) panelComCod.Visibility = Visibility.Collapsed;
                if (panelGlobalDesc02 != null) panelGlobalDesc02.Visibility = Visibility.Collapsed;
                if (btnDeploySp != null) btnDeploySp.Visibility = Visibility.Collapsed;
                if (txtWizardTitle != null) txtWizardTitle.Text = "🚀 Data Migration Pipeline";
                if (txtDbLabel != null) txtDbLabel.Text = "Target System: Ready for Migration";
                if (borderStepProcBadge != null) borderStepProcBadge.Visibility = Visibility.Collapsed;
                if (txtDoneBannerDetail != null)
                    txtDoneBannerDetail.Text = "Core Khatians, plots, and owners data have been successfully migrated and committed into the core system.";
            }
        }

        private void DataMigrationWizardWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExecutingStep || _isAutoRunning)
            {
                var result = MessageBox.Show(
                    "⚠️ The data migration pipeline is currently executing on SQL Server!\n\n" +
                    "Closing the wizard now will interrupt active migration operations and may leave the core database in an inconsistent state.\n\n" +
                    "Are you sure you want to abort the migration and exit?",
                    "Warning: Migration in Progress",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }
        }

        #endregion
    }
}
