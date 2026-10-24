using DBAccess;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Data;
using System.Net.Http;
using System.Text;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static DBAccess.DataMigrationEntityView00;
using static FontConverter.Converter;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Window = System.Windows.Window;
using SolidBrush = System.Windows.Media.SolidColorBrush;
using MediaColor = System.Windows.Media.Color;


namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //public MainWindow()
        //{
        //    InitializeComponent();
        //}

        //private const string _baseConnectionString = "Server=GOLDENJUNK\\SQL2K22ENT;Database=softbd_tangail_07012026;User ID=sa;Password=1234;MultipleActiveResultSets=False;TrustServerCertificate=True;Connect Timeout=600";
        private string _baseConnectionString;
        private string _DbName;
        private string _ServerName;
        private const string _BaseDbName = "DLRSLSINF_4103PRSDB";
        private string _Schema;
        private List<string> _selectedTables = new();
        DataTable table1Data = new DataTable();
        DataTable table2Data = new DataTable();
        DataTable tablRowCountData = new DataTable();

        private List<tblLinfo01> tblLinfo01List = new();
        private List<tblLinfo02> tblLinfo02List = new();

        private string tblInf01RowCount = string.Empty;
        private string tblInf02RowCount = string.Empty;

        // ── Sequential Workflow Completion Flags ──
        // Intermediate Database (DLRS_Migration_Intrm) Ready Flag
        private bool _isIntermediateDbReady = false;

        // Step completion flags:
        // _isStage2Completed represents Step 1 (Copy & Stage to DLRS_Migration_Intrm)
        // _isStage3Completed represents Step 2 (Unicode Font Conversion & Intermediate DB Update)
        // _isStage4Completed represents Step 3 (Core Data Migration)
        private bool _isStage2Completed = false;
        private bool _isStage3Completed = false;
        private bool _isStage4Completed = false;

        // Current active pipeline step (1 = Copy, 2 = Font Convert, 3 = Migration)
        // Only the current step panel is displayed to the user
        private int _currentActiveStep = 1;

        // Failsafe tracking for active long-running operations
        private bool _isOperationRunning = false;
        private string _currentRunningOperationName = string.Empty;
        private CancellationTokenSource? _currentOperationCts = null;

        // Execution control mode: default false = 1-Click Automated Pipeline; true = Manual Step-by-Step
        private bool _isManualModeEnabled = false;
        private bool _isAutoPipelinePaused = false;
        private readonly System.Diagnostics.Stopwatch _autoPipelineStopwatch = new();
        private System.Windows.Threading.DispatcherTimer? _autoPipelineTimer = null;

        public MainWindow()
        {
            InitializeComponent();
            cmbAuth.SelectedIndex = 0;
            cmbAuth.SelectionChanged += Auth_SelectionChanged;

            UpdateThemeButton();
            ApplyRoleView();
            ApplyControlModeView();

            ThemeManager.ThemeChanged += OnThemeChanged;
            Closing += MainWindow_Closing;
            Closed += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;

            UpdateWorkflowState();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isOperationRunning)
            {
                var result = System.Windows.MessageBox.Show(
                    $"⚠️ Operation in Progress!\n\n" +
                    $"Currently executing: [{_currentRunningOperationName}]\n\n" +
                    "Closing the application now will interrupt active database staging/conversion operations and may leave data in an incomplete state.\n\n" +
                    "Are you sure you want to abort the current operation and exit?",
                    "Warning: Operation in Progress",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                try
                {
                    _currentOperationCts?.Cancel();
                }
                catch { }
            }
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
        }

        private void OnThemeChanged(AppTheme theme)
        {
            UpdateThemeButton();
            UpdateWorkflowState();
        }

        private void UpdateThemeButton()
        {
            if (txtThemeIcon == null || txtThemeLabel == null) return;
            bool isDark = ThemeManager.CurrentTheme == AppTheme.Dark;
            txtThemeIcon.Text = isDark ? "🌙" : "☀️";
            txtThemeLabel.Text = isDark ? "Dark" : "Light";
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // ── TOP-RIGHT CONTROL MODE SWITCHER (Auto Execution Default vs Manual) ──
        // ─────────────────────────────────────────────────────────────────────────────
        private void BtnToggleControlMode_Click(object sender, RoutedEventArgs e)
        {
            _isManualModeEnabled = !_isManualModeEnabled;
            ApplyControlModeView();
        }

        private void ApplyControlModeView()
        {
            if (btnToggleControlMode == null) return;

            if (_isManualModeEnabled)
            {
                if (txtControlModeIcon != null) txtControlModeIcon.Text = "🖐";
                if (txtControlModeText != null)
                {
                    txtControlModeText.Text = "Manual Control Mode";
                    txtControlModeText.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                }
                btnToggleControlMode.ToolTip = "Currently in Manual Step-by-Step execution mode. Click to switch to 1-Click Automated Pipeline (Default).";

                if (cardAutoPipelineHero != null) cardAutoPipelineHero.Visibility = Visibility.Collapsed;
                if (panelManualStep1 != null) panelManualStep1.Visibility = Visibility.Visible;
            }
            else
            {
                if (txtControlModeIcon != null) txtControlModeIcon.Text = "⚡";
                if (txtControlModeText != null)
                {
                    txtControlModeText.Text = "Auto Execution (Default)";
                    txtControlModeText.Foreground = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                }
                btnToggleControlMode.ToolTip = "Currently in 1-Click Automated Pipeline mode (default). Click to switch to Manual Step-by-Step.";

                if (cardAutoPipelineHero != null) cardAutoPipelineHero.Visibility = Visibility.Visible;
                if (panelManualStep1 != null) panelManualStep1.Visibility = Visibility.Collapsed;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // ── PIPELINE EXECUTION & VALUE MAPPING LOG VIEWER ──
        // ─────────────────────────────────────────────────────────────────────────────
        private void BtnOpenPipelineLogs_Click(object sender, RoutedEventArgs e)
        {
            PipelineLogViewerWindow.ShowLogViewer(this);
        }

        /// <summary>
        /// Converts Adarshalipi ANSI text to Unicode and verifies value mapping integrity,
        /// logging any anomalies, unmapped character codes, or truncations into PipelineExecutionLogger.
        /// </summary>
        private string ConvertAndCheckMapping(string? raw, string table, string column, string recordId)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string converted = ConvertToAdarshalipi(raw);
            PipelineExecutionLogger.CheckAndLogMappingAnomaly("Step 2 (Font Conversion)", table, column, recordId, raw, converted);
            return converted;
        }

        /// <summary>
        /// Pre-check: Inspects [DLRS_Migration_Intrm].dbo.tblUnitMigrationLog to determine if any UnitCod
        /// in the selected source database is already recorded with Status = 'COMPLETED'.
        /// If already migrated, displays an alert banner, disables migration buttons, and blocks execution.
        /// </summary>
        private async Task<bool> CheckSelectedDatabaseMigrationStatusAsync()
        {
            if (string.IsNullOrEmpty(_DbName) || string.IsNullOrEmpty(_baseConnectionString) || !_isIntermediateDbReady)
            {
                if (borderAlreadyMigratedNotice != null) borderAlreadyMigratedNotice.Visibility = Visibility.Collapsed;
                return false;
            }

            try
            {
                var (isMigrated, migratedUnits, msg) = await IntermediateDbService.CheckUnitAlreadyMigratedAsync(_baseConnectionString, _DbName);

                if (isMigrated)
                {
                    if (borderAlreadyMigratedNotice != null) borderAlreadyMigratedNotice.Visibility = Visibility.Visible;
                    if (txtAlreadyMigratedReason != null)
                        txtAlreadyMigratedReason.Text = $"Unit code(s) [{string.Join(", ", migratedUnits)}] in database '{_DbName}' are ALREADY logged as COMPLETED in tblUnitMigrationLog. Migration cannot proceed to prevent duplicate records.";

                    if (btnStartFullAuto != null) btnStartFullAuto.IsEnabled = false;
                    if (btnCopyToIntrmDb != null) btnCopyToIntrmDb.IsEnabled = false;

                    PipelineExecutionLogger.LogWarning("Pre-Check", msg, table: "Source DB", recordId: string.Join(",", migratedUnits));
                    return true;
                }
                else
                {
                    if (borderAlreadyMigratedNotice != null) borderAlreadyMigratedNotice.Visibility = Visibility.Collapsed;
                    return false;
                }
            }
            catch (Exception ex)
            {
                PipelineExecutionLogger.LogWarning("Pre-Check", $"Pre-check warning: {ex.Message}");
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // ── SEQUENTIAL STEP PANEL NAVIGATION (Shows ONLY current active step) ──
        // ─────────────────────────────────────────────────────────────────────────────
        public void ShowStep(int step)
        {
            if (step == 2 && !_isStage2Completed)
            {
                System.Windows.MessageBox.Show("Please complete Step 1 (Copy to Intermediate DB) first.", "Step 1 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (step == 3 && !_isStage3Completed)
            {
                System.Windows.MessageBox.Show("Please complete Step 2 (Unicode Font Conversion & Intermediate DB Update) first.", "Step 2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentActiveStep = step;
            UpdateWorkflowState();
        }

        private void BtnGoToStep2_Click(object sender, RoutedEventArgs e) => ShowStep(2);
        private void BtnBackToStep1_Click(object sender, RoutedEventArgs e) => ShowStep(1);
        private void BtnGoToStep3_Click(object sender, RoutedEventArgs e) => ShowStep(3);
        private void BtnBackToStep2_Click(object sender, RoutedEventArgs e) => ShowStep(2);

        private void ChipStep1_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowStep(1);
        private void ChipStep2_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isStage2Completed) ShowStep(2);
            else System.Windows.MessageBox.Show("Please complete Step 1 (Copy to Intermediate DB) first.", "Step 1 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        private void ChipStep3_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isStage3Completed) ShowStep(3);
            else System.Windows.MessageBox.Show("Please complete Step 2 (Unicode Font Conversion) first.", "Step 2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ApplyRoleView()
        {
            if (UserSession.IsAdmin)
            {
                if (borderUserRoleBadge != null)
                {
                    borderUserRoleBadge.Visibility = Visibility.Visible;
                    borderUserRoleBadge.Background = new SolidBrush(MediaColor.FromRgb(6, 95, 70));
                }
                if (txtUserRoleBadge != null)
                    txtUserRoleBadge.Text = "👤 Admin (Full Access)";

                if (panelAdminDelayControl != null)
                    panelAdminDelayControl.Visibility = Visibility.Visible;

                // Admin view: Keep remaining time ETA, conversion speed, and record count
                if (lblConversionEta != null)
                    lblConversionEta.Visibility = Visibility.Visible;

                if (lblConversionSpeed != null)
                    lblConversionSpeed.Visibility = Visibility.Visible;

                if (lblConversionCount != null)
                    lblConversionCount.Visibility = Visibility.Visible;
            }
            else
            {
                // Remove the standard mode text label for the normal user
                if (borderUserRoleBadge != null)
                    borderUserRoleBadge.Visibility = Visibility.Collapsed;
                if (txtUserRoleBadge != null)
                    txtUserRoleBadge.Text = string.Empty;

                if (panelAdminDelayControl != null)
                    panelAdminDelayControl.Visibility = Visibility.Collapsed;

                // Standard User view: Remove remaining time (ETA), speed, and record counts.
                // Keep strictly just the progress % and the progression bar.
                if (lblConversionEta != null)
                    lblConversionEta.Visibility = Visibility.Collapsed;

                if (lblConversionSpeed != null)
                    lblConversionSpeed.Visibility = Visibility.Collapsed;

                if (lblConversionCount != null)
                    lblConversionCount.Visibility = Visibility.Collapsed;

                if (txtStep1Desc != null)
                    txtStep1Desc.Text = "Stages data from the selected source database into [DLRS_Migration_Intrm]. Source DB is never altered.";

                if (txtStage4Desc != null)
                    txtStage4Desc.Text = "Migrates land records, plots, owners, maps, and boundary data into the core system.";
            }
        }

        private void Auth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isSqlAuth = ((ComboBoxItem)cmbAuth.SelectedItem).Content.ToString() == "SQL";
            txtUser.Visibility = isSqlAuth ? Visibility.Visible : Visibility.Collapsed;
            txtPassword.Visibility = isSqlAuth ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------
        // CONNECT TO SERVER
        // ------------------------------
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            // Reset inline status
            txtConnectError.Visibility = Visibility.Collapsed;
            txtConnectError.Text       = string.Empty;

            _ServerName = txtServer.Text.Trim();
            if (string.IsNullOrEmpty(_ServerName))
            {
                ShowConnectError("⚠ Please enter a server name.");
                return;
            }

            try
            {
                _baseConnectionString = BuildConnectionString("master");

                using var conn = new SqlConnection(_baseConnectionString);
                await conn.OpenAsync();

                // Update server status summary and Pre-Step server banner
                if (txtServerStatusSummary != null)
                {
                    txtServerStatusSummary.Text = $"🟢 Connected to \"{_ServerName}\"";
                    txtServerStatusSummary.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
                if (txtPreStepServerName != null)
                {
                    txtPreStepServerName.Text = $"🟢 {_ServerName}";
                    txtPreStepServerName.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
                if (btnRunPreStep != null)
                {
                    btnRunPreStep.IsEnabled = true;
                    btnRunPreStep.ToolTip = $"Restore or attach database directly to {_ServerName}";
                }

                // Check and validate Intermediate Database (DLRS_Migration_Intrm)
                await CheckAndValidateIntermediateDbAsync();

                if (_isIntermediateDbReady)
                {
                    LoadDatabases(conn);
                    txtConnectError.Foreground = System.Windows.Media.Brushes.LimeGreen;
                    txtConnectError.Text       = "✔ Connected successfully.";
                    txtConnectError.Visibility = Visibility.Visible;
                }
                else
                {
                    lstDatabases.Items.Clear();
                    lstDatabases.IsEnabled = false;
                    txtConnectError.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                    txtConnectError.Text       = $"⚠ Server connected. Initialize [{AppConfig.IntermediateDbName}] below to proceed.";
                    txtConnectError.Visibility = Visibility.Visible;
                }
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                // SqlException error numbers:
                //   2    = server not found / connection refused
                //   53   = network path not found
                //   4060 = database does not exist
                //   18456 = login failed (wrong credentials)
                //   233  = no connection established (named pipes)
                string msg = sqlEx.Number switch
                {
                    2 or 53      => $"⚠ Cannot reach server \"{_ServerName}\". Check the server name and network connectivity.",
                    4060         => $"⚠ Database not found on \"{_ServerName}\". Verify the database name.",
                    18456        => "⚠ Login failed — incorrect username or password.",
                    233          => $"⚠ Connection refused by \"{_ServerName}\". Ensure SQL Server is running and remote connections are allowed.",
                    _            => $"⚠ SQL Error {sqlEx.Number}: {sqlEx.Message}"
                };
                ShowConnectError(msg);
            }
            catch (Exception ex)
            {
                ShowConnectError($"⚠ Unexpected error: {ex.Message}");
            }
        }

        /// <summary>Shows a red inline error message next to the Connect button and resets status banners.</summary>
        private void ShowConnectError(string message)
        {
            txtConnectError.Foreground = System.Windows.Media.Brushes.Tomato;
            txtConnectError.Text       = message;
            txtConnectError.Visibility = Visibility.Visible;

            if (txtServerStatusSummary != null)
            {
                txtServerStatusSummary.Text = "⚪ Disconnected";
                txtServerStatusSummary.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
            }
            if (txtPreStepServerName != null)
            {
                txtPreStepServerName.Text = "🔒 Not Connected (Connect in Stage 1 above)";
                txtPreStepServerName.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
            }
            if (btnRunPreStep != null)
            {
                btnRunPreStep.IsEnabled = false;
                btnRunPreStep.ToolTip = "Please connect to a SQL Server in Stage 1 above first.";
            }

            if (txtIntrmDbStatus != null)
            {
                _isIntermediateDbReady = false;
                txtIntrmDbStatus.Text = "⚪ Disconnected";
                txtIntrmDbStatus.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
            }
            if (btnCreateOrRepairIntrmDb != null)
                btnCreateOrRepairIntrmDb.Visibility = Visibility.Collapsed;
            if (btnViewIntrmLogs != null)
                btnViewIntrmLogs.Visibility = Visibility.Collapsed;
            if (txtIntrmDbDetail != null)
                txtIntrmDbDetail.Visibility = Visibility.Collapsed;
            if (borderIntrmBlockedNotice != null)
                borderIntrmBlockedNotice.Visibility = Visibility.Collapsed;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // ─────────────────────────────────────────────────────────────────────────────
        // PERMANENT INTERMEDIATE DATABASE (DLRS_Migration_Intrm) CHECK & MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────────────
        private async Task CheckAndValidateIntermediateDbAsync()
        {
            if (string.IsNullOrEmpty(_baseConnectionString))
            {
                _isIntermediateDbReady = false;
                txtIntrmDbStatus.Text = "⚪ Not Connected";
                txtIntrmDbStatus.Foreground = new SolidBrush(MediaColor.FromRgb(148, 163, 184));
                btnCreateOrRepairIntrmDb.Visibility = Visibility.Collapsed;
                if (btnViewIntrmLogs != null) btnViewIntrmLogs.Visibility = Visibility.Collapsed;
                txtIntrmDbDetail.Visibility = Visibility.Collapsed;
                borderIntrmBlockedNotice.Visibility = Visibility.Collapsed;
                UpdateWorkflowState();
                return;
            }

            try
            {
                // 1. Check if DB exists
                var (exists, compatLevel) = await IntermediateDbService.CheckDatabaseExistsAsync(_baseConnectionString);

                if (!exists)
                {
                    _isIntermediateDbReady = false;
                    txtIntrmDbStatus.Text = "❌ Missing / Not Found";
                    txtIntrmDbStatus.Foreground = new SolidBrush(MediaColor.FromRgb(239, 68, 68));

                    txtBtnCreateIntrmDb.Text = $"⚡ Create {AppConfig.IntermediateDbName}";
                    btnCreateOrRepairIntrmDb.Visibility = Visibility.Visible;
                    btnCreateOrRepairIntrmDb.IsEnabled = true;
                    if (btnViewIntrmLogs != null) btnViewIntrmLogs.Visibility = Visibility.Collapsed;

                    txtIntrmDbDetail.Text = $"Database [{AppConfig.IntermediateDbName}] does not exist on this server. It is mandatory for staging and migration.";
                    txtIntrmDbDetail.Foreground = new SolidBrush(MediaColor.FromRgb(248, 113, 113));
                    txtIntrmDbDetail.Visibility = Visibility.Visible;

                    txtIntrmBlockedReason.Text = $"Access Blocked: You must click '⚡ Create {AppConfig.IntermediateDbName}' to initialize the staging database before selecting databases or running migrations.";
                    borderIntrmBlockedNotice.Visibility = Visibility.Visible;

                    lstDatabases.IsEnabled = false;
                    UpdateWorkflowState();
                    return;
                }

                // 2. Database exists -> Validate schema
                var (isValid, errors) = await IntermediateDbService.ValidateSchemaAsync(_baseConnectionString);

                if (!isValid)
                {
                    _isIntermediateDbReady = false;
                    txtIntrmDbStatus.Text = "⚠️ Invalid Schema / Missing Tables";
                    txtIntrmDbStatus.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));

                    txtBtnCreateIntrmDb.Text = $"🔧 Repair {AppConfig.IntermediateDbName}";
                    btnCreateOrRepairIntrmDb.Visibility = Visibility.Visible;
                    btnCreateOrRepairIntrmDb.IsEnabled = true;
                    if (btnViewIntrmLogs != null) btnViewIntrmLogs.Visibility = Visibility.Collapsed;

                    string errSummary = string.Join("; ", errors);
                    txtIntrmDbDetail.Text = $"Schema issues detected: {errSummary}";
                    txtIntrmDbDetail.Foreground = new SolidBrush(MediaColor.FromRgb(251, 191, 36));
                    txtIntrmDbDetail.Visibility = Visibility.Visible;

                    txtIntrmBlockedReason.Text = $"Access Blocked: [{AppConfig.IntermediateDbName}] has missing tables or incompatible column types ({errors.Count} issue(s)). Click '🔧 Repair {AppConfig.IntermediateDbName}' to update schema.";
                    borderIntrmBlockedNotice.Visibility = Visibility.Visible;

                    lstDatabases.IsEnabled = false;
                    UpdateWorkflowState();
                    return;
                }

                // 3. Valid and Ready
                _isIntermediateDbReady = true;
                txtIntrmDbStatus.Text = $"✔ Ready & Verified (Compat: {(compatLevel > 0 ? compatLevel.ToString() : "130+")})";
                txtIntrmDbStatus.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));

                btnCreateOrRepairIntrmDb.Visibility = Visibility.Collapsed;
                if (btnViewIntrmLogs != null) btnViewIntrmLogs.Visibility = Visibility.Visible;

                txtIntrmDbDetail.Text = "Verified all required tables (tblHalsabek, tblLinfo01, tblLinfo02, tblUnitMigrationLog) and Unicode data types.";
                txtIntrmDbDetail.Foreground = new SolidBrush(MediaColor.FromRgb(148, 163, 184));
                txtIntrmDbDetail.Visibility = Visibility.Visible;

                borderIntrmBlockedNotice.Visibility = Visibility.Collapsed;
                lstDatabases.IsEnabled = true;
                UpdateWorkflowState();

                // Run pre-check: inspect if unit code in selected database is already migrated in tblUnitMigrationLog
                await CheckSelectedDatabaseMigrationStatusAsync();
            }
            catch (Exception ex)
            {
                _isIntermediateDbReady = false;
                txtIntrmDbStatus.Text = "❌ Check Failed";
                txtIntrmDbStatus.Foreground = new SolidBrush(MediaColor.FromRgb(239, 68, 68));
                txtIntrmDbDetail.Text = $"Error verifying [{AppConfig.IntermediateDbName}]: {ex.Message}";
                txtIntrmDbDetail.Visibility = Visibility.Visible;
                borderIntrmBlockedNotice.Visibility = Visibility.Visible;
                lstDatabases.IsEnabled = false;
                UpdateWorkflowState();
            }
        }

        private async void BtnCreateOrRepairIntrmDb_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_baseConnectionString))
            {
                System.Windows.MessageBox.Show("Please connect to a SQL Server instance first.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnCreateOrRepairIntrmDb.IsEnabled = false;
            pbIntrmDbAction.Visibility = Visibility.Visible;
            txtIntrmDbStatus.Text = "⏳ Creating / Repairing database...";
            txtIntrmDbStatus.Foreground = new SolidBrush(MediaColor.FromRgb(59, 130, 246));

            try
            {
                await IntermediateDbService.CreateOrRepairDatabaseAsync(_baseConnectionString);

                // Re-validate after creation
                await CheckAndValidateIntermediateDbAsync();

                if (_isIntermediateDbReady)
                {
                    // Refresh database list now that the database exists
                    using (var conn = new SqlConnection(_baseConnectionString))
                    {
                        await conn.OpenAsync();
                        LoadDatabases(conn);
                    }

                    System.Windows.MessageBox.Show(
                        $"Permanent Intermediate Database [{AppConfig.IntermediateDbName}] has been successfully created and verified on \"{_ServerName}\".\n\nTables created/verified:\n • tblHalsabek (with convsts)\n • tblLinfo01 (with Unicode KRemark, ROWID, convsts)\n • tblLinfo02 (with Unicode OwnName/addr1-10, RowId, convsts)\n • tblUnitMigrationLog (audit tracking table)\n\nYou may now select a source database to proceed.",
                        "Database Ready",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _isIntermediateDbReady = false;
                txtIntrmDbStatus.Text = "❌ Creation / Repair Failed";
                txtIntrmDbStatus.Foreground = new SolidBrush(MediaColor.FromRgb(239, 68, 68));
                txtIntrmDbDetail.Text = $"Error: {ex.Message}";
                txtIntrmDbDetail.Visibility = Visibility.Visible;

                System.Windows.MessageBox.Show(
                    $"Failed to create or repair [{AppConfig.IntermediateDbName}]:\n\n{ex.Message}\n\nPlease check server permissions (CREATE DATABASE / ALTER permissions required).",
                    "Database Creation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                pbIntrmDbAction.Visibility = Visibility.Collapsed;
                btnCreateOrRepairIntrmDb.IsEnabled = true;
            }
        }

        private void BtnViewIntrmLogs_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_baseConnectionString))
            {
                System.Windows.MessageBox.Show("Please connect to a SQL Server instance first.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new IntermediateDbLogsDialog(_baseConnectionString)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        #region PRE-STEP: Restore / Attach Database
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Toggles the visible panel when the user switches between Restore and Attach modes.
        /// </summary>
        private void RdMode_Checked(object sender, RoutedEventArgs e)
        {
            if (panelRestoreMode == null || panelAttachMode == null) return;
            bool isRestore = rdRestore.IsChecked == true;
            panelRestoreMode.Visibility = isRestore ? Visibility.Visible : Visibility.Collapsed;
            panelAttachMode.Visibility  = isRestore ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Opens a file dialog for the .bak backup and pre-fills the DB name.</summary>
        private void BtnBrowseBak_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQL Server Backup (*.bak)|*.bak|All files (*.*)|*.*",
                Title  = "Select SQL Server Backup File"
            };
            if (dlg.ShowDialog() == true)
            {
                txtBakPath.Text = dlg.FileName;
                // Pre-fill DB name from the backup filename (without extension)
                if (string.IsNullOrWhiteSpace(txtRestoreDbName.Text))
                    txtRestoreDbName.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        /// <summary>Opens a file dialog for the .mdf data file and pre-fills the DB name.</summary>
        private void BtnBrowseMdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQL Server Data File (*.mdf)|*.mdf|All files (*.*)|*.*",
                Title  = "Select SQL Server Data File (.mdf)"
            };
            if (dlg.ShowDialog() == true)
            {
                txtMdfPath.Text = dlg.FileName;
                // Pre-fill DB name from the MDF filename (without extension)
                if (string.IsNullOrWhiteSpace(txtAttachDbName.Text))
                    txtAttachDbName.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        /// <summary>Opens a file dialog for the optional .ldf log file.</summary>
        private void BtnBrowseLdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQL Server Log File (*.ldf)|*.ldf|All files (*.*)|*.*",
                Title  = "Select SQL Server Log File (.ldf) — Optional"
            };
            if (dlg.ShowDialog() == true)
                txtLdfPath.Text = dlg.FileName;
        }

        /// <summary>
        /// Main handler for the "Restore / Attach Database" button.
        /// Validates inputs, checks for duplicate DB names, then runs the operation async.
        /// On success, refreshes the database list and auto-selects the new DB.
        /// </summary>
        private async void BtnRunPreStep_Click(object sender, RoutedEventArgs e)
        {
            // Guard: user must have clicked Connect first
            if (string.IsNullOrEmpty(_ServerName) || string.IsNullOrEmpty(_baseConnectionString))
            {
                ShowPreStepStatus("⚠ Please connect to a SQL Server first (use the Server field in Stage 1 above).", isError: true);
                return;
            }

            bool   isRestore = rdRestore.IsChecked == true;
            string dbName    = isRestore ? txtRestoreDbName.Text.Trim() : txtAttachDbName.Text.Trim();

            // ── Input validation ──
            if (string.IsNullOrWhiteSpace(dbName))
            {
                ShowPreStepStatus("⚠ Please enter a database name.", isError: true);
                return;
            }

            var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                _BaseDbName, "master", "tempdb", "model", "msdb"
            };
            if (reservedNames.Contains(dbName))
            {
                ShowPreStepStatus($"⚠ The database name \"{dbName}\" is reserved and cannot be used.", isError: true);
                return;
            }

            if (isRestore)
            {
                if (string.IsNullOrWhiteSpace(txtBakPath.Text) || !System.IO.File.Exists(txtBakPath.Text))
                {
                    ShowPreStepStatus("⚠ Please select a valid .bak backup file.", isError: true);
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(txtMdfPath.Text) || !System.IO.File.Exists(txtMdfPath.Text))
                {
                    ShowPreStepStatus("⚠ Please select a valid .mdf data file.", isError: true);
                    return;
                }
            }

            // ── Check if the target DB name already exists on the server ──
            try
            {
                using var checkConn = new SqlConnection(_baseConnectionString);
                await checkConn.OpenAsync();
                using var checkCmd  = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @n", checkConn);
                checkCmd.Parameters.AddWithValue("@n", dbName);
                int exists = (int)(await checkCmd.ExecuteScalarAsync() ?? 0);
                if (exists > 0)
                {
                    ShowPreStepStatus($"⚠ A database named \"{dbName}\" already exists on this server. Please choose a different name.", isError: true);
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowPreStepStatus($"⚠ Could not verify database name: {ex.Message}", isError: true);
                return;
            }

            // ── Start the operation ──
            btnRunPreStep.IsEnabled  = false;
            pbPreStep.Visibility     = Visibility.Visible;
            pbPreStep.Value          = 0;
            ShowPreStepStatus("⏳ Starting...", isError: false);

            _isOperationRunning = true;
            _currentRunningOperationName = isRestore ? $"Restoring backup [{txtBakPath.Text}] as database [{dbName}]" : $"Attaching data file [{txtMdfPath.Text}] as database [{dbName}]";
            _currentOperationCts = new CancellationTokenSource();

            try
            {
                if (isRestore)
                    await RunRestoreAsync(txtBakPath.Text, dbName);
                else
                    await RunAttachAsync(txtMdfPath.Text, txtLdfPath.Text.Trim(), dbName);

                // ── Success: refresh list and auto-select the new DB ──
                ShowPreStepStatus($"✔ Database \"{dbName}\" is ready. Refreshing database list…", isError: false);
                pbPreStep.Value = 100;

                using var refreshConn = new SqlConnection(_baseConnectionString);
                await refreshConn.OpenAsync();
                LoadDatabases(refreshConn);

                // Allow the ListBox to render before we try to select
                await Task.Delay(150);
                lstDatabases.SelectedItem = dbName;
            }
            catch (Exception ex)
            {
                // Provide friendly messages for common failure modes
                string friendlyMsg;
                if (ex.Message.Contains("Access is denied") || ex.Message.Contains("permission"))
                    friendlyMsg = $"⚠ Permission denied. Ensure the SQL Server service account has read/write access to the file(s).\n\nDetail: {ex.Message}";
                else if (ex.Message.Contains("operating system error"))
                    friendlyMsg = $"⚠ File system error. Check that the path is accessible from the server.\n\nDetail: {ex.Message}";
                else
                    friendlyMsg = $"⚠ Operation failed: {ex.Message}";

                ShowPreStepStatus(friendlyMsg, isError: true);
                pbPreStep.Value = 0;
            }
            finally
            {
                _isOperationRunning = false;
                _currentRunningOperationName = string.Empty;
                btnRunPreStep.IsEnabled = true;
            }
        }

        /// <summary>
        /// Restores a .bak backup to the server.
        /// Steps:
        ///   1. Reads the server's default data/log folder paths (SERVERPROPERTY or registry fallback).
        ///   2. Reads the logical file list from the backup (RESTORE FILELISTONLY).
        ///   3. Builds a RESTORE DATABASE command with MOVE clauses targeting the default folders.
        ///   4. Executes it in a background thread and reports STATS=10 progress via InfoMessage.
        /// </summary>
        private async Task RunRestoreAsync(string bakPath, string dbName)
        {
            string dataPath = string.Empty, logPath = string.Empty;
            var    fileList = new List<(string logicalName, string fileType)>();

            // ── Step 1: Discover default data/log directories ──
            using (var conn = new SqlConnection(_baseConnectionString))
            {
                await conn.OpenAsync();

                // 1. Try SERVERPROPERTY (SQL Server 2012+)
                try
                {
                    using (var cmd = new SqlCommand(
                        "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(512)), " +
                        "       CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(512))", conn))
                    {
                        using var dr = await cmd.ExecuteReaderAsync();
                        if (await dr.ReadAsync())
                        {
                            dataPath = dr[0] != DBNull.Value ? dr[0]?.ToString() ?? string.Empty : string.Empty;
                            logPath  = dr[1] != DBNull.Value ? dr[1]?.ToString() ?? string.Empty : string.Empty;
                        }
                    }
                }
                catch { }

                // 2. If not found, try the SQL Server registry key
                if (string.IsNullOrWhiteSpace(dataPath))
                {
                    try
                    {
                        using var cmd = new SqlCommand(
                            "EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', " +
                            "N'Software\\Microsoft\\MSSQLServer\\MSSQLServer', N'DefaultData'", conn);
                        using var dr = await cmd.ExecuteReaderAsync();
                        if (await dr.ReadAsync()) dataPath = dr[1]?.ToString() ?? string.Empty;
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(logPath))
                {
                    try
                    {
                        using var cmd = new SqlCommand(
                            "EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', " +
                            "N'Software\\Microsoft\\MSSQLServer\\MSSQLServer', N'DefaultLog'", conn);
                        using var dr = await cmd.ExecuteReaderAsync();
                        if (await dr.ReadAsync()) logPath = dr[1]?.ToString() ?? string.Empty;
                    }
                    catch { }
                }

                // 3. Fallback: derive folder from master database's MDF path
                if (string.IsNullOrWhiteSpace(dataPath) || string.IsNullOrWhiteSpace(logPath))
                {
                    try
                    {
                        using var cmd = new SqlCommand(
                            "SELECT physical_name FROM sys.master_files WHERE database_id = 1 AND type_desc = 'ROWS'", conn);
                        var mdf      = (string?)await cmd.ExecuteScalarAsync() ?? string.Empty;
                        string fallback = System.IO.Path.GetDirectoryName(mdf) ?? @"C:\";
                        if (string.IsNullOrWhiteSpace(dataPath)) dataPath = fallback;
                        if (string.IsNullOrWhiteSpace(logPath))  logPath  = fallback;
                    }
                    catch { }
                }

                // ── Step 2: Read logical file names from the backup ──
                using (var cmd = new SqlCommand(
                    $"RESTORE FILELISTONLY FROM DISK = N'{bakPath.Replace("'", "''")}'", conn))
                {
                    using var dr = await cmd.ExecuteReaderAsync();
                    while (await dr.ReadAsync())
                        fileList.Add((dr["LogicalName"].ToString()!, dr["Type"].ToString()!));
                }
            }

            // ── Step 3: Build the RESTORE DATABASE command with MOVE clauses ──
            var sb = new System.Text.StringBuilder();
            sb.Append($"RESTORE DATABASE [{dbName.Replace("]", "]]")}] FROM DISK = N'{bakPath.Replace("'", "''")}' WITH ");
            int dataIndex = 0;
            int logIndex = 0;
            foreach (var (logName, fileType) in fileList)
            {
                bool isLog = fileType.Equals("L", StringComparison.OrdinalIgnoreCase);
                string ext;
                if (isLog)
                {
                    ext = logIndex == 0 ? "_log.ldf" : $"_log{logIndex}.ldf";
                    logIndex++;
                }
                else
                {
                    ext = dataIndex == 0 ? ".mdf" : $"_{dataIndex}.ndf";
                    dataIndex++;
                }
                string folder = isLog ? logPath : dataPath;
                string dest   = System.IO.Path.Combine(folder, $"{dbName}{ext}");
                sb.Append($"MOVE N'{logName.Replace("'", "''")}' TO N'{dest.Replace("'", "''")}', ");
            }
            // STATS=10 → SQL Server sends an InfoMessage every 10% progress
            sb.Append("STATS = 10, REPLACE");

            // ── Step 4: Execute in background; capture STATS progress via InfoMessage ──
            await Task.Run(() =>
            {
                using var conn = new SqlConnection(_baseConnectionString);

                // InfoMessage fires for each STATS progress report during restore
                conn.InfoMessage += (s, ev) =>
                {
                    var msg = ev.Message;
                    if (int.TryParse(
                        System.Text.RegularExpressions.Regex.Match(msg, @"(\d+) percent").Groups[1].Value,
                        out int pct))
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            pbPreStep.Value = pct;
                            ShowPreStepStatus($"⏳ Restoring… {pct}%", isError: false);
                        });
                    }
                };
                conn.FireInfoMessageEventOnUserErrors = true;
                conn.Open();

                using var cmd = new SqlCommand(sb.ToString(), conn) { CommandTimeout = 7200 };
                cmd.ExecuteNonQuery();
            });
        }

        /// <summary>
        /// Attaches a .mdf (and optionally .ldf) file as a new database.
        /// If no .ldf is provided (or the file doesn't exist), uses FOR ATTACH_REBUILD_LOG
        /// so SQL Server creates a fresh log file rather than failing.
        /// </summary>
        private async Task RunAttachAsync(string mdfPath, string ldfPath, string dbName)
        {
            string escapedDb = dbName.Replace("]", "]]");
            string sql;
            if (!string.IsNullOrWhiteSpace(ldfPath) && System.IO.File.Exists(ldfPath))
            {
                // Attach with the existing log file
                sql = $"CREATE DATABASE [{escapedDb}] " +
                      $"ON (FILENAME = N'{mdfPath.Replace("'", "''")}'), " +
                      $"   (FILENAME = N'{ldfPath.Replace("'", "''")}') " +
                      "FOR ATTACH";
            }
            else
            {
                // No LDF provided — rebuild the log automatically
                sql = $"CREATE DATABASE [{escapedDb}] " +
                      $"ON (FILENAME = N'{mdfPath.Replace("'", "''")}') " +
                      "FOR ATTACH_REBUILD_LOG";
            }

            Dispatcher.Invoke(() => ShowPreStepStatus("⏳ Attaching database…", isError: false));

            await Task.Run(() =>
            {
                using var conn = new SqlConnection(_baseConnectionString);
                conn.Open();
                using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 600 };
                cmd.ExecuteNonQuery();
            });
        }

        /// <summary>
        /// Displays a status message in the Pre-Step card.
        /// Green teal for success, tomato red for errors.
        /// </summary>
        private void ShowPreStepStatus(string message, bool isError)
        {
            txtPreStepStatus.Text       = message;
            txtPreStepStatus.Foreground = isError
                ? System.Windows.Media.Brushes.Tomato
                : new SolidBrush(MediaColor.FromRgb(52, 211, 153));
            txtPreStepStatus.Visibility = Visibility.Visible;
        }

        #endregion
        // ─────────────────────────────────────────────────────────────────────────────

        //private string BuildConnectionString(string database)
        //{
        //    var server = txtServer.Text.Trim();
        //    if (string.IsNullOrEmpty(server))
        //        throw new Exception("Server name is required.");

        //    var auth = ((ComboBoxItem)cmbAuth.SelectedItem).Content.ToString();

        //    if (auth == "Windows")
        //    {
        //        return $"Server={server};Database={database};Integrated Security=True;";
        //    }
        //    else
        //    {
        //        if (string.IsNullOrEmpty(txtUser.Text))
        //            throw new Exception("User name required.");

        //        return $"Server={server};Database={database};User Id={txtUser.Text};Password={txtPassword.Password};";
        //    }
        //}

        private string BuildConnectionString(string database)
        {
            var auth = ((ComboBoxItem)cmbAuth.SelectedItem).Content.ToString();

            if (auth == "Windows")
            {
                return $"Server={_ServerName};Database={database};Integrated Security=True;" +
                       "Encrypt=True;TrustServerCertificate=True;";
            }
            else
            {
                return $"Server={_ServerName};Database={database};User Id={txtUser.Text};Password={txtPassword.Password};" +
                       "Encrypt=True;TrustServerCertificate=True;";
            }
        }

        // ------------------------------
        // LOAD DATABASES
        // ------------------------------
        private void LoadDatabases(SqlConnection conn)
        {
            lstDatabases.Items.Clear();

            string sql = @"
                SELECT name
                FROM sys.databases
                WHERE state_desc = 'ONLINE'
                  AND name NOT IN ('master', 'tempdb', 'model', 'msdb', 'DLRS_Migration_Intrm')
                ORDER BY name";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
                lstDatabases.Items.Add(reader.GetString(0));
        }

        // ── WORKFLOW STATE MANAGEMENT & SEQUENTIAL ENFORCEMENT ──
        private void UpdateWorkflowState()
        {
            bool hasDb = _isIntermediateDbReady && !string.IsNullOrEmpty(_DbName);
            bool isDark = ThemeManager.CurrentTheme == AppTheme.Dark;

            var inactiveBorder = new SolidBrush(isDark ? MediaColor.FromRgb(51, 65, 85) : MediaColor.FromRgb(203, 213, 225));
            var inactiveBadge  = new SolidBrush(isDark ? MediaColor.FromRgb(100, 116, 139) : MediaColor.FromRgb(148, 163, 184));
            var inactiveText   = new SolidBrush(isDark ? MediaColor.FromRgb(148, 163, 184) : MediaColor.FromRgb(71, 85, 105));

            // ── PANEL VISIBILITY: ONLY the current active step panel is visible! ──
            if (panelStep1CopyToIntrm != null)
                panelStep1CopyToIntrm.Visibility = (_currentActiveStep == 1) ? Visibility.Visible : Visibility.Collapsed;
            if (panelStep2FontConvert != null)
                panelStep2FontConvert.Visibility = (_currentActiveStep == 2) ? Visibility.Visible : Visibility.Collapsed;
            if (panelStep3Migration != null)
                panelStep3Migration.Visibility = (_currentActiveStep == 3) ? Visibility.Visible : Visibility.Collapsed;

            // ── STEP 1 CHIP & STEP 1 PANEL CONTROLS ──
            if (!_isIntermediateDbReady)
            {
                lstDatabases.IsEnabled = false;
                if (btnCopyToIntrmDb != null) btnCopyToIntrmDb.IsEnabled = false;
                if (btnDataMigrationWizard != null) btnDataMigrationWizard.IsEnabled = false;

                if (!string.IsNullOrEmpty(_ServerName))
                {
                    chipStep1.BorderBrush = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                    borderChip1Badge.Background = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                    txtChip1Num.Text = "!";
                    txtChip1Status.Text = "Initialize Intermediate DB";
                    txtChip1Status.Foreground = new SolidBrush(MediaColor.FromRgb(251, 191, 36));
                }
                else
                {
                    chipStep1.BorderBrush = inactiveBorder;
                    borderChip1Badge.Background = inactiveBadge;
                    txtChip1Num.Text = "1";
                    txtChip1Status.Text = "Connect to server";
                    txtChip1Status.Foreground = inactiveText;
                }

                if (borderStep1LockBanner != null) borderStep1LockBanner.Visibility = Visibility.Visible;
            }
            else if (!hasDb)
            {
                chipStep1.BorderBrush = new SolidBrush(MediaColor.FromRgb(59, 130, 246));
                borderChip1Badge.Background = new SolidBrush(MediaColor.FromRgb(59, 130, 246));
                txtChip1Num.Text = "1";
                txtChip1Status.Text = "Select source DB";
                txtChip1Status.Foreground = inactiveText;

                if (btnCopyToIntrmDb != null) btnCopyToIntrmDb.IsEnabled = false;
                if (borderStep1LockBanner != null) borderStep1LockBanner.Visibility = Visibility.Visible;
            }
            else if (_isStage2Completed)
            {
                // Step 1 completed
                chipStep1.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                borderChip1Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                txtChip1Num.Text = "✔";
                txtChip1Status.Text = $"✔ Staged: {_DbName}";
                txtChip1Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));

                if (borderStep1LockBanner != null) borderStep1LockBanner.Visibility = Visibility.Collapsed;
                if (borderStep1CompletedBanner != null) borderStep1CompletedBanner.Visibility = Visibility.Visible;
                if (btnGoToStep2 != null) btnGoToStep2.Visibility = Visibility.Visible;
                if (btnCopyToIntrmDb != null)
                {
                    btnCopyToIntrmDb.IsEnabled = true;
                    if (txtBtnCopyToIntrm != null) txtBtnCopyToIntrm.Text = "🔁 Re-Copy Source Data to Intermediate DB";
                }
            }
            else
            {
                // Ready to copy
                chipStep1.BorderBrush = (_currentActiveStep == 1)
                    ? new SolidBrush(MediaColor.FromRgb(2, 132, 199))
                    : new SolidBrush(MediaColor.FromRgb(59, 130, 246));
                borderChip1Badge.Background = new SolidBrush(MediaColor.FromRgb(2, 132, 199));
                txtChip1Num.Text = "1";
                txtChip1Status.Text = $"Ready: {_DbName}";
                txtChip1Status.Foreground = new SolidBrush(MediaColor.FromRgb(56, 189, 248));

                if (borderStep1LockBanner != null) borderStep1LockBanner.Visibility = Visibility.Collapsed;
                if (borderStep1CompletedBanner != null) borderStep1CompletedBanner.Visibility = Visibility.Collapsed;
                if (btnGoToStep2 != null) btnGoToStep2.Visibility = Visibility.Collapsed;
                if (btnCopyToIntrmDb != null)
                {
                    btnCopyToIntrmDb.IsEnabled = true;
                    if (txtBtnCopyToIntrm != null) txtBtnCopyToIntrm.Text = "Copy Source Data to DLRS_Migration_Intrm (Single Step)";
                }
            }

            // ── 1-CLICK AUTOMATED PIPELINE HERO STATE ──
            if (btnStartFullAuto != null)
            {
                if (!_isIntermediateDbReady)
                {
                    btnStartFullAuto.IsEnabled = false;
                    if (txtFullAutoStatus != null)
                    {
                        txtFullAutoStatus.Text = "Waiting: Initialize [DLRS_Migration_Intrm] before starting.";
                        txtFullAutoStatus.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                    }
                }
                else if (!hasDb)
                {
                    btnStartFullAuto.IsEnabled = false;
                    if (txtFullAutoStatus != null)
                    {
                        txtFullAutoStatus.Text = "Waiting: Connect to server & select a source database to begin.";
                        txtFullAutoStatus.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                    }
                }
                else if (_isStage4Completed)
                {
                    btnStartFullAuto.IsEnabled = false;
                    if (txtFullAutoStatus != null)
                    {
                        txtFullAutoStatus.Text = "✔ Migration Pipeline Already Completed for this database.";
                        txtFullAutoStatus.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                    }
                    if (borderAutoCompletedBanner != null) borderAutoCompletedBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    btnStartFullAuto.IsEnabled = !_isOperationRunning;
                    if (txtFullAutoStatus != null && !_isOperationRunning)
                    {
                        txtFullAutoStatus.Text = $"Ready: 1-Click Automated Pipeline for [{_DbName}]";
                        txtFullAutoStatus.Foreground = new SolidBrush(MediaColor.FromRgb(56, 189, 248));
                    }
                }
            }

            // Visual 3-phase pill updates on the hero card
            if (borderAutoPhase1 != null)
            {
                if (_isStage2Completed)
                {
                    borderAutoPhase1.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                    if (txtAutoPhase1Icon != null) txtAutoPhase1Icon.Text = "✔";
                    if (txtAutoPhase1Text != null) txtAutoPhase1Text.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
                else
                {
                    borderAutoPhase1.BorderBrush = (_currentActiveStep == 1) ? new SolidBrush(MediaColor.FromRgb(59, 130, 246)) : inactiveBorder;
                    if (txtAutoPhase1Icon != null) txtAutoPhase1Icon.Text = "📥";
                    if (txtAutoPhase1Text != null) txtAutoPhase1Text.Foreground = inactiveText;
                }
            }

            if (borderAutoPhase2 != null)
            {
                if (_isStage3Completed)
                {
                    borderAutoPhase2.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                    if (txtAutoPhase2Icon != null) txtAutoPhase2Icon.Text = "✔";
                    if (txtAutoPhase2Text != null) txtAutoPhase2Text.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
                else
                {
                    borderAutoPhase2.BorderBrush = (_currentActiveStep == 2) ? new SolidBrush(MediaColor.FromRgb(16, 185, 129)) : inactiveBorder;
                    if (txtAutoPhase2Icon != null) txtAutoPhase2Icon.Text = "🔤";
                    if (txtAutoPhase2Text != null) txtAutoPhase2Text.Foreground = inactiveText;
                }
            }

            if (borderAutoPhase3 != null)
            {
                if (_isStage4Completed)
                {
                    borderAutoPhase3.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                    if (txtAutoPhase3Icon != null) txtAutoPhase3Icon.Text = "✔";
                    if (txtAutoPhase3Text != null) txtAutoPhase3Text.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
                else
                {
                    borderAutoPhase3.BorderBrush = (_currentActiveStep == 3) ? new SolidBrush(MediaColor.FromRgb(139, 92, 246)) : inactiveBorder;
                    if (txtAutoPhase3Icon != null) txtAutoPhase3Icon.Text = "🚀";
                    if (txtAutoPhase3Text != null) txtAutoPhase3Text.Foreground = inactiveText;
                }
            }

            // ── STEP 2 CHIP & STEP 2 PANEL CONTROLS ──
            if (!_isStage2Completed)
            {
                chipStep2.BorderBrush = inactiveBorder;
                borderChip2Badge.Background = inactiveBadge;
                txtChip2Num.Text = "2";
                txtChip2Status.Text = "🔒 Locked (Needs Step 1)";
                txtChip2Status.Foreground = inactiveText;

                if (borderStage3LockBanner != null) borderStage3LockBanner.Visibility = Visibility.Visible;
                if (borderStage3UnlockedBanner != null) borderStage3UnlockedBanner.Visibility = Visibility.Collapsed;
                if (lstTables != null) lstTables.IsEnabled = false;
                if (fetchDataForSelectedTables != null) fetchDataForSelectedTables.IsEnabled = false;
                if (fontConverterBTN != null) fontConverterBTN.IsEnabled = false;
                if (updateDatabaseBTN != null) updateDatabaseBTN.IsEnabled = false;
                if (btnGoToStep3 != null) btnGoToStep3.Visibility = Visibility.Collapsed;
            }
            else if (_isStage3Completed)
            {
                chipStep2.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                borderChip2Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                txtChip2Num.Text = "✔";
                txtChip2Status.Text = "✔ Converted & Updated";
                txtChip2Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));

                if (borderStage3LockBanner != null) borderStage3LockBanner.Visibility = Visibility.Collapsed;
                if (borderStage3UnlockedBanner != null) borderStage3UnlockedBanner.Visibility = Visibility.Visible;
                if (lstTables != null) lstTables.IsEnabled = false;
                if (fetchDataForSelectedTables != null) fetchDataForSelectedTables.IsEnabled = false;
                if (fontConverterBTN != null) fontConverterBTN.IsEnabled = false;
                if (updateDatabaseBTN != null)
                {
                    updateDatabaseBTN.IsEnabled = false;
                    updateDatabaseBTN.Content = "✔ Intermediate DB Updated with Unicode (Locked)";
                }
                if (btnGoToStep3 != null) btnGoToStep3.Visibility = Visibility.Visible;
            }
            else
            {
                chipStep2.BorderBrush = (_currentActiveStep == 2)
                    ? new SolidBrush(MediaColor.FromRgb(16, 185, 129))
                    : new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                borderChip2Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                txtChip2Num.Text = "2";
                txtChip2Status.Text = "Ready to Convert";
                txtChip2Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));

                if (borderStage3LockBanner != null) borderStage3LockBanner.Visibility = Visibility.Collapsed;
                if (borderStage3UnlockedBanner != null) borderStage3UnlockedBanner.Visibility = Visibility.Visible;
                if (lstTables != null) lstTables.IsEnabled = true;
                if (fetchDataForSelectedTables != null) fetchDataForSelectedTables.IsEnabled = true;
                if (btnGoToStep3 != null) btnGoToStep3.Visibility = Visibility.Collapsed;
            }

            // ── STEP 3 CHIP & STEP 3 PANEL CONTROLS ──
            if (_isStage4Completed)
            {
                chipStep3.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                borderChip3Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                txtChip3Num.Text = "✔";
                txtChip3Status.Text = "✔ Migration Completed";
                txtChip3Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));

                if (borderStage4LockBanner != null) borderStage4LockBanner.Visibility = Visibility.Collapsed;
                if (borderStage4UnlockedBanner != null) borderStage4UnlockedBanner.Visibility = Visibility.Collapsed;
                if (borderStage4CompletedBanner != null) borderStage4CompletedBanner.Visibility = Visibility.Visible;
                if (btnDataMigrationWizard != null) btnDataMigrationWizard.IsEnabled = true;
                if (btnViewMigratedUnits != null) btnViewMigratedUnits.Visibility = Visibility.Visible;
                if (txtBtnDataMigrationWizard != null) txtBtnDataMigrationWizard.Text = "✔ Migration Completed (Review Pipeline)";
            }
            else if (!_isStage3Completed)
            {
                chipStep3.BorderBrush = inactiveBorder;
                borderChip3Badge.Background = inactiveBadge;
                txtChip3Num.Text = "3";
                txtChip3Status.Text = "🔒 Locked (Needs Step 2)";
                txtChip3Status.Foreground = inactiveText;

                if (borderStage4LockBanner != null) borderStage4LockBanner.Visibility = Visibility.Visible;
                if (borderStage4UnlockedBanner != null) borderStage4UnlockedBanner.Visibility = Visibility.Collapsed;
                if (borderStage4CompletedBanner != null) borderStage4CompletedBanner.Visibility = Visibility.Collapsed;
                if (btnDataMigrationWizard != null) btnDataMigrationWizard.IsEnabled = false;
                if (btnViewMigratedUnits != null) btnViewMigratedUnits.Visibility = Visibility.Collapsed;
                if (txtBtnDataMigrationWizard != null) txtBtnDataMigrationWizard.Text = "🚀 Launch Data Migration Wizard";
            }
            else
            {
                chipStep3.BorderBrush = (_currentActiveStep == 3)
                    ? new SolidBrush(MediaColor.FromRgb(139, 92, 246))
                    : new SolidBrush(MediaColor.FromRgb(196, 181, 253));
                borderChip3Badge.Background = new SolidBrush(MediaColor.FromRgb(139, 92, 246));
                txtChip3Num.Text = "3";
                txtChip3Status.Text = "🚀 Ready for Migration";
                txtChip3Status.Foreground = new SolidBrush(MediaColor.FromRgb(196, 181, 253));

                if (borderStage4LockBanner != null) borderStage4LockBanner.Visibility = Visibility.Collapsed;
                if (borderStage4UnlockedBanner != null) borderStage4UnlockedBanner.Visibility = Visibility.Visible;
                if (borderStage4CompletedBanner != null) borderStage4CompletedBanner.Visibility = Visibility.Collapsed;
                if (btnDataMigrationWizard != null) btnDataMigrationWizard.IsEnabled = true;
                if (btnViewMigratedUnits != null) btnViewMigratedUnits.Visibility = Visibility.Collapsed;
                if (txtBtnDataMigrationWizard != null) txtBtnDataMigrationWizard.Text = "🚀 Launch Data Migration Wizard";
            }
        }

        /// <summary>Checks whether the selected database already has all Stage 2 prerequisites met.</summary>
        private async Task<bool> CheckDatabasePreparedAsync(string connectionString)
        {
            try
            {
                return await Task.Run(() =>
                {
                    using var conn = new SqlConnection(connectionString);
                    conn.Open();

                    // 1. Check compatibility level >= 130
                    int compatLevel = 0;
                    using (var cmd = new SqlCommand("SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME();", conn))
                    {
                        var val = cmd.ExecuteScalar();
                        if (val != null && val != DBNull.Value)
                            compatLevel = Convert.ToInt32(val);
                    }
                    if (compatLevel < 130)
                        return false;

                    // 2. Check RowId in tblLinfo02
                    int rowIdCount = 0;
                    using (var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLinfo02') AND name = 'RowId';", conn))
                    {
                        rowIdCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                    }
                    if (rowIdCount == 0)
                        return false;

                    // 3. Check convsts in tblHalsabek, tblLinfo01, tblLinfo02
                    int convstsCount = 0;
                    using (var cmd = new SqlCommand(@"
                        SELECT COUNT(*) 
                        FROM sys.columns 
                        WHERE name = 'convsts' 
                          AND object_id IN (OBJECT_ID('dbo.tblHalsabek'), OBJECT_ID('dbo.tblLinfo01'), OBJECT_ID('dbo.tblLinfo02'));", conn))
                    {
                        convstsCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                    }
                    if (convstsCount < 3)
                        return false;

                    // 4. Check KRemark in tblLinfo01 is NVARCHAR (system_type_id = 231)
                    int kremarkType = 0;
                    using (var cmd = new SqlCommand("SELECT system_type_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLinfo01') AND name = 'KRemark';", conn))
                    {
                        kremarkType = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                    }
                    if (kremarkType != 231)
                        return false;

                    return true;
                });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detects if a string contains any Bengali Unicode characters (code point range U+0980 to U+09FF).
        /// Returns false for legacy ANSI/ASCII strings (which only contain characters <= 0xFF).
        /// </summary>
        public static bool ContainsBengaliUnicode(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '\u0980' && c <= '\u09FF')
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Inspects the target tables (tblLinfo01, tblLinfo02) in the source database to identify
        /// whether they already contain Bengali Unicode records or have been migrated (convsts='SUCCESS').
        /// Returns a list of table names that are already converted.
        /// </summary>
        private async Task<List<string>> CheckTablesAlreadyConvertedInDbAsync(string connectionString)
        {
            return await Task.Run(() =>
            {
                var convertedTables = new List<string>();
                try
                {
                    using var conn = new SqlConnection(connectionString);
                    conn.Open();

                    // 1. Check dbo.tblLinfo01
                    bool linfo01Converted = false;
                    try
                    {
                        using (var cmd = new SqlCommand(@"
                            IF OBJECT_ID('dbo.tblLinfo01') IS NOT NULL AND COL_LENGTH('dbo.tblLinfo01', 'convsts') IS NOT NULL
                            BEGIN
                                SELECT TOP 1 1 FROM dbo.tblLinfo01 WHERE convsts = 'SUCCESS';
                            END", conn))
                        {
                            var val = cmd.ExecuteScalar();
                            if (val != null && val != DBNull.Value && Convert.ToInt32(val) == 1)
                                linfo01Converted = true;
                        }

                        if (!linfo01Converted)
                        {
                            using var cmd = new SqlCommand(@"
                                IF OBJECT_ID('dbo.tblLinfo01') IS NOT NULL
                                BEGIN
                                    SELECT TOP 30 KRemark FROM dbo.tblLinfo01 WHERE KRemark IS NOT NULL AND LEN(LTRIM(RTRIM(KRemark))) > 0;
                                END", conn);
                            using var reader = cmd.ExecuteReader();
                            while (reader.Read())
                            {
                                if (ContainsBengaliUnicode(reader.GetString(0)))
                                {
                                    linfo01Converted = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    if (linfo01Converted)
                        convertedTables.Add("dbo.tblLinfo01");

                    // 2. Check dbo.tblLinfo02
                    bool linfo02Converted = false;
                    try
                    {
                        using (var cmd = new SqlCommand(@"
                            IF OBJECT_ID('dbo.tblLinfo02') IS NOT NULL AND COL_LENGTH('dbo.tblLinfo02', 'convsts') IS NOT NULL
                            BEGIN
                                SELECT TOP 1 1 FROM dbo.tblLinfo02 WHERE convsts = 'SUCCESS';
                            END", conn))
                        {
                            var val = cmd.ExecuteScalar();
                            if (val != null && val != DBNull.Value && Convert.ToInt32(val) == 1)
                                linfo02Converted = true;
                        }

                        if (!linfo02Converted)
                        {
                            using var cmd = new SqlCommand(@"
                                IF OBJECT_ID('dbo.tblLinfo02') IS NOT NULL
                                BEGIN
                                    SELECT TOP 30 OwnName FROM dbo.tblLinfo02 WHERE OwnName IS NOT NULL AND LEN(LTRIM(RTRIM(OwnName))) > 0;
                                END", conn);
                            using var reader = cmd.ExecuteReader();
                            while (reader.Read())
                            {
                                if (ContainsBengaliUnicode(reader.GetString(0)))
                                {
                                    linfo02Converted = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    if (linfo02Converted)
                        convertedTables.Add("dbo.tblLinfo02");
                }
                catch { }

                return convertedTables;
            });
        }

        /// <summary>
        /// Checks if dbo.tblLinfo01 and dbo.tblLinfo02 in the source database already have
        /// convsts = 'SUCCESS' indicating that the SP_LIS_DATA_MIGRATION procedure was already executed.
        /// </summary>
        private async Task<bool> CheckMigrationAlreadyCompletedInDbAsync(string connectionString)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var conn = new SqlConnection(connectionString);
                    conn.Open();

                    bool linfo01Done = false;
                    bool linfo02Done = false;

                    using (var cmd = new SqlCommand(@"
                        IF OBJECT_ID('dbo.tblLinfo01') IS NOT NULL AND COL_LENGTH('dbo.tblLinfo01', 'convsts') IS NOT NULL
                        BEGIN
                            SELECT TOP 1 1 FROM dbo.tblLinfo01 WHERE convsts = 'SUCCESS';
                        END
                        ELSE IF OBJECT_ID('dbo.tblHalsabek') IS NOT NULL AND COL_LENGTH('dbo.tblHalsabek', 'convsts') IS NOT NULL
                        BEGIN
                            SELECT TOP 1 1 FROM dbo.tblHalsabek WHERE convsts = 'SUCCESS';
                        END", conn))
                    {
                        var val = cmd.ExecuteScalar();
                        if (val != null && val != DBNull.Value && Convert.ToInt32(val) == 1)
                            linfo01Done = true;
                    }

                    using (var cmd = new SqlCommand(@"
                        IF OBJECT_ID('dbo.tblLinfo02') IS NOT NULL AND COL_LENGTH('dbo.tblLinfo02', 'convsts') IS NOT NULL
                        BEGIN
                            SELECT TOP 1 1 FROM dbo.tblLinfo02 WHERE convsts = 'SUCCESS';
                        END", conn))
                    {
                        var val = cmd.ExecuteScalar();
                        if (val != null && val != DBNull.Value && Convert.ToInt32(val) == 1)
                            linfo02Done = true;
                    }

                    return linfo01Done || linfo02Done;
                }
                catch
                {
                    return false;
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // ── 1-CLICK AUTOMATED PIPELINE ORCHESTRATION (DEFAULT HANDS-FREE EXECUTION) ──
        // ─────────────────────────────────────────────────────────────────────────────
        private async void BtnStartFullAuto_Click(object sender, RoutedEventArgs e)
        {
            await RunCompleteAutoPipelineAsync();
        }

        private void BtnPauseFullAuto_Click(object sender, RoutedEventArgs e)
        {
            _isAutoPipelinePaused = !_isAutoPipelinePaused;
            if (btnPauseFullAuto != null)
                btnPauseFullAuto.Content = _isAutoPipelinePaused ? "▶ Resume" : "⏸ Pause";
        }

        private void BtnStopFullAuto_Click(object sender, RoutedEventArgs e)
        {
            var res = System.Windows.MessageBox.Show(
                "Are you sure you want to stop the automated pipeline?\n\nAny currently executing SQL command will finish, and subsequent pipeline steps will be cancelled.",
                "Confirm Pipeline Stop",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    _currentOperationCts?.Cancel();
                }
                catch { }
            }
        }

        private async Task RunCompleteAutoPipelineAsync()
        {
            if (string.IsNullOrEmpty(_baseConnectionString))
            {
                PipelineAlertWindow.ShowAlert(
                    this,
                    "Server Not Connected",
                    "Preparation",
                    "Please enter your SQL Server name and click 'Connect' in the left Preparation panel before starting the pipeline.",
                    "Connection string is not set.",
                    allowSkip: false);
                return;
            }

            if (string.IsNullOrEmpty(_DbName))
            {
                PipelineAlertWindow.ShowAlert(
                    this,
                    "Source Database Required",
                    "Preparation",
                    "Please select a legacy source database from the list in the left panel to proceed.",
                    "No source database selected.",
                    allowSkip: false);
                return;
            }

            if (!_isIntermediateDbReady)
            {
                PipelineAlertWindow.ShowAlert(
                    this,
                    "Intermediate DB Not Ready",
                    "Preparation",
                    $"The intermediate staging database [{AppConfig.IntermediateDbName}] is not initialized. Please click 'Create DB' or 'Validate' in the left panel.",
                    "Intermediate DB validation failed or not executed.",
                    allowSkip: false);
                return;
            }

            // Pre-check: Verify unit code in selected database has not already been migrated
            if (await CheckSelectedDatabaseMigrationStatusAsync())
            {
                PipelineAlertWindow.ShowAlert(
                    this,
                    "Migration Blocked: Unit Already Migrated",
                    "Pre-Check (tblUnitMigrationLog)",
                    $"Unit code(s) in database [{_DbName}] have already been migrated and recorded as COMPLETED in tblUnitMigrationLog.\n\nAutomated pipeline will NOT proceed for this unit code to prevent duplicate data insertion.",
                    "Audit log entry with Status = 'COMPLETED' exists in [DLRS_Migration_Intrm].dbo.tblUnitMigrationLog.",
                    allowSkip: false);
                PipelineExecutionLogger.LogError("Pre-Check", $"Automated pipeline blocked: Unit code(s) in [{_DbName}] already migrated.");
                return;
            }

            if (_isOperationRunning) return;

            _isOperationRunning = true;
            _currentRunningOperationName = $"Automated Pipeline (Stage -> Convert -> Migrate) for [{_DbName}]";
            _currentOperationCts = new CancellationTokenSource();
            var ct = _currentOperationCts.Token;

            if (btnStartFullAuto != null) btnStartFullAuto.IsEnabled = false;
            if (panelAutoControls != null) panelAutoControls.Visibility = Visibility.Visible;
            if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 2;
            if (borderAutoCompletedBanner != null) borderAutoCompletedBanner.Visibility = Visibility.Collapsed;

            _isAutoPipelinePaused = false;
            if (btnPauseFullAuto != null) btnPauseFullAuto.Content = "⏸ Pause";

            _autoPipelineStopwatch.Restart();
            _autoPipelineTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _autoPipelineTimer.Tick += (s, e) =>
            {
                if (txtFullAutoTimer != null)
                    txtFullAutoTimer.Text = $"⏱ {_autoPipelineStopwatch.Elapsed:mm\\:ss}";
            };
            _autoPipelineTimer.Start();

            try
            {
                // ══════════════════════════════════════════════════════════════════
                // PHASE 1: STAGE DATA FROM SOURCE INTO DLRS_Migration_Intrm
                // ══════════════════════════════════════════════════════════════════
                _currentActiveStep = 1;
                UpdateWorkflowState();

                if (txtFullAutoStatus != null)
                {
                    txtFullAutoStatus.Text = "Phase 1/3: Staging tables from source into DLRS_Migration_Intrm...";
                    txtFullAutoStatus.Foreground = new SolidBrush(MediaColor.FromRgb(56, 189, 248));
                }
                if (txtFullAutoDetail != null)
                    txtFullAutoDetail.Text = $"Copying tblHalsabek, tblLinfo01, and tblLinfo02 from [{_DbName}]...";
                if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 5;

                bool phase1Success = false;
                int hsb = 0, l01 = 0, l02 = 0;

                while (!phase1Success)
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        (hsb, l01, l02) = await IntermediateDbService.StageDataFromSourceAsync(_baseConnectionString, _DbName);
                        phase1Success = true;
                    }
                    catch (Exception ex)
                    {
                        var (action, _) = PipelineAlertWindow.ShowAlert(
                            this,
                            "Phase 1 (Staging) Error",
                            "Step 1: Staging Tables",
                            $"Failed to copy tables from [{_DbName}] to [{AppConfig.IntermediateDbName}]:\n\n{ex.Message}",
                            ex.ToString(),
                            allowSkip: false);

                        if (action == PipelineAlertAction.Retry)
                            continue;
                        else
                            return; // Abort
                    }
                }

                _isStage2Completed = true;
                if (txtStep1CompletedDetail != null)
                    txtStep1CompletedDetail.Text = $"Staged {hsb:N0} Halsabek, {l01:N0} Linfo01, {l02:N0} Linfo02 records into [{AppConfig.IntermediateDbName}].";
                if (borderStep1CompletedBanner != null) borderStep1CompletedBanner.Visibility = Visibility.Visible;
                if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 30;

                await LoadIntermediateTablesAsync();
                await Task.Delay(AppConfig.AutoRunStepDelayMs, ct);

                while (_isAutoPipelinePaused && !ct.IsCancellationRequested)
                    await Task.Delay(200, ct);

                // ══════════════════════════════════════════════════════════════════
                // PHASE 2: UNICODE FONT CONVERSION & INTERMEDIATE DB UPDATE
                // ══════════════════════════════════════════════════════════════════
                _currentActiveStep = 2;
                UpdateWorkflowState();

                if (txtFullAutoStatus != null)
                    txtFullAutoStatus.Text = "Phase 2/3: Fetching staged records from DLRS_Migration_Intrm...";
                if (txtFullAutoDetail != null)
                    txtFullAutoDetail.Text = "Loading tblLinfo01 and tblLinfo02 for Unicode font conversion...";
                if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 35;

                bool fetchSuccess = false;
                while (!fetchSuccess)
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        await FetchStagedDataForAutoAsync(ct);
                        fetchSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        var (action, _) = PipelineAlertWindow.ShowAlert(
                            this,
                            "Phase 2 (Data Fetch) Error",
                            "Step 2: Staged Record Fetch",
                            $"Failed to fetch staged records from [{AppConfig.IntermediateDbName}]:\n\n{ex.Message}",
                            ex.ToString(),
                            allowSkip: false);

                        if (action == PipelineAlertAction.Retry)
                            continue;
                        else
                            return; // Abort
                    }
                }

                if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 45;

                while (_isAutoPipelinePaused && !ct.IsCancellationRequested)
                    await Task.Delay(200, ct);

                // Perform in-memory Unicode font conversion
                if (txtFullAutoStatus != null)
                    txtFullAutoStatus.Text = "Phase 2/3: Converting font to Unicode in real-time...";
                if (txtFullAutoDetail != null)
                    txtFullAutoDetail.Text = $"Converting text for {tblLinfo01List.Count + tblLinfo02List.Count:N0} records...";

                await ConvertFontForAutoAsync(ct);
                if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 60;

                int mappingWarns = PipelineExecutionLogger.WarningCount;
                if (mappingWarns > 0 && txtFullAutoDetail != null)
                {
                    txtFullAutoDetail.Text = $"Converted {tblLinfo01List.Count + tblLinfo02List.Count:N0} records ({mappingWarns} mapping notice(s) recorded in [📋 Logs]).";
                }

                while (_isAutoPipelinePaused && !ct.IsCancellationRequested)
                    await Task.Delay(200, ct);

                // Commit Unicode updates to DLRS_Migration_Intrm
                if (txtFullAutoStatus != null)
                    txtFullAutoStatus.Text = "Phase 2/3: Writing converted Unicode records to DLRS_Migration_Intrm...";
                if (txtFullAutoDetail != null)
                    txtFullAutoDetail.Text = "Committing batch updates via SQL JSON chunks...";

                bool updateSuccess = false;
                while (!updateSuccess)
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        await CommitUnicodeUpdateForAutoAsync(ct);
                        updateSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        var (action, _) = PipelineAlertWindow.ShowAlert(
                            this,
                            "Phase 2 (Database Update) Error",
                            "Step 2: Commit Unicode Data",
                            $"Failed to write Unicode data to [{AppConfig.IntermediateDbName}]:\n\n{ex.Message}",
                            ex.ToString(),
                            allowSkip: false);

                        if (action == PipelineAlertAction.Retry)
                            continue;
                        else
                            return; // Abort
                    }
                }

                _isStage3Completed = true;
                if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 75;
                await Task.Delay(AppConfig.AutoRunStepDelayMs, ct);

                while (_isAutoPipelinePaused && !ct.IsCancellationRequested)
                    await Task.Delay(200, ct);

                // ══════════════════════════════════════════════════════════════════
                // PHASE 3: SEQUENTIAL STORED PROCEDURES MIGRATION TO CORE DB
                // ══════════════════════════════════════════════════════════════════
                _currentActiveStep = 3;
                UpdateWorkflowState();

                if (txtFullAutoStatus != null)
                    txtFullAutoStatus.Text = "Phase 3/3: Executing sequential core migration stored procedures...";
                if (txtFullAutoDetail != null)
                    txtFullAutoDetail.Text = $"Migrating data from [{AppConfig.IntermediateDbName}] into [{_BaseDbName}]...";

                string targetCs = BuildConnectionString(_BaseDbName);

                // Auto-deploy stored procedures if missing
                try
                {
                    await StoredProcedureDeployer.EnsureProceduresDeployedAsync(targetCs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StoredProcedureDeployer notice: {ex.Message}");
                }

                if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 80;

                // Launch Data Migration Wizard popup in Auto-Run mode so the user sees live execution!
                var wizard = new DataMigrationWizardWindow(targetCs, AppConfig.IntermediateDbName, autoStart: true)
                {
                    Owner = this
                };

                bool logged = false;
                wizard.MigrationCompleted += async () =>
                {
                    _isStage4Completed = true;
                    UpdateWorkflowState();
                    if (!logged && !string.IsNullOrEmpty(_DbName) && !string.IsNullOrEmpty(_baseConnectionString))
                    {
                        logged = true;
                        await IntermediateDbService.LogCompletedUnitsAsync(_baseConnectionString, _DbName, "COMPLETED");
                    }
                };

                wizard.ShowDialog();

                if (!wizard.IsMigrationCompleted)
                {
                    if (txtFullAutoStatus != null)
                    {
                        txtFullAutoStatus.Text = "⚠️ Migration wizard was closed or stopped before completion.";
                        txtFullAutoStatus.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                    }
                    if (txtFullAutoDetail != null)
                        txtFullAutoDetail.Text = "You can re-open Step 3 manually at any time to run or retry pending steps.";

                    try
                    {
                        if (!string.IsNullOrEmpty(_DbName) && !string.IsNullOrEmpty(_baseConnectionString))
                        {
                            await IntermediateDbService.LogCompletedUnitsAsync(
                                _baseConnectionString, 
                                _DbName, 
                                status: "INCOMPLETE", 
                                remarks: "Migration wizard closed before all required steps finished.");
                        }
                    }
                    catch { }
                    return;
                }

                // ══════════════════════════════════════════════════════════════════
                // COMPLETION
                // ══════════════════════════════════════════════════════════════════
                if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 100;
                _isStage4Completed = true;
                _currentActiveStep = 1; // Return view to Step 1 summary
                UpdateWorkflowState();

                if (txtFullAutoStatus != null)
                {
                    txtFullAutoStatus.Text = "🎉 COMPLETE AUTOMATED PIPELINE FINISHED SUCCESSFULLY!";
                    txtFullAutoStatus.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
                if (txtFullAutoDetail != null)
                    txtFullAutoDetail.Text = $"All records from [{_DbName}] staged, converted to Unicode, and migrated into [{_BaseDbName}].";

                if (borderAutoCompletedBanner != null)
                {
                    borderAutoCompletedBanner.Visibility = Visibility.Visible;
                    if (txtAutoCompletedDetail != null)
                        txtAutoCompletedDetail.Text = $"Successfully completed all 3 phases in {_autoPipelineStopwatch.Elapsed:mm\\:ss}. Unit migration recorded in audit log.";
                }

                // Audit log in Intermediate DB
                try
                {
                    await IntermediateDbService.LogCompletedUnitsAsync(_baseConnectionString, _DbName, "COMPLETED");
                }
                catch { }

                System.Windows.MessageBox.Show(
                    $"🎉 Complete Conversion & Migration Pipeline Finished!\n\n" +
                    $"Source Database: {_DbName}\n" +
                    $"Intermediate DB: {AppConfig.IntermediateDbName}\n" +
                    $"Target Core DB: {_BaseDbName}\n" +
                    $"Total Time Elapsed: {_autoPipelineStopwatch.Elapsed:mm\\:ss}\n\n" +
                    "All data has been staged, converted to Unicode, and migrated successfully.",
                    "Pipeline Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                if (txtFullAutoStatus != null)
                {
                    txtFullAutoStatus.Text = "⚠️ Automated pipeline was stopped by user.";
                    txtFullAutoStatus.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                }
                PipelineExecutionLogger.LogWarning("Automated Pipeline", "Pipeline was cancelled by user.");
                try
                {
                    if (!string.IsNullOrEmpty(_DbName) && !string.IsNullOrEmpty(_baseConnectionString))
                    {
                        await IntermediateDbService.LogCompletedUnitsAsync(
                            _baseConnectionString, 
                            _DbName, 
                            status: "CANCELLED", 
                            remarks: "Pipeline was stopped by user.");
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                PipelineExecutionLogger.LogError("Automated Pipeline", $"Pipeline failed: {ex.Message}");
                try
                {
                    if (!string.IsNullOrEmpty(_DbName) && !string.IsNullOrEmpty(_baseConnectionString))
                    {
                        await IntermediateDbService.LogCompletedUnitsAsync(
                            _baseConnectionString, 
                            _DbName, 
                            status: "FAILED", 
                            remarks: ex.Message.Length > 450 ? ex.Message.Substring(0, 450) : ex.Message);
                    }
                }
                catch { }

                PipelineAlertWindow.ShowAlert(
                    this,
                    "Pipeline Error",
                    "Automated Pipeline",
                    $"An unexpected error occurred during pipeline execution:\n\n{ex.Message}",
                    ex.ToString(),
                    allowSkip: false);
            }
            finally
            {
                _autoPipelineTimer?.Stop();
                _isOperationRunning = false;
                _currentRunningOperationName = string.Empty;
                _currentOperationCts = null;

                if (btnStartFullAuto != null)
                    btnStartFullAuto.IsEnabled = (!_isStage4Completed && !string.IsNullOrEmpty(_DbName));
                if (panelAutoControls != null)
                    panelAutoControls.Visibility = Visibility.Collapsed;
            }
        }

        private async Task FetchStagedDataForAutoAsync(CancellationToken ct)
        {
            tblLinfo01List.Clear();
            tblLinfo02List.Clear();
            tablRowCountData.Clear();

            var fixedConnectionString = BuildConnectionString(_BaseDbName);

            // Fetch tblLinfo01 from DLRS_Migration_Intrm
            await Task.Run(() =>
            {
                using var conn = new SqlConnection(fixedConnectionString);
                conn.Open();
                using var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 300
                };
                cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "SLCTLINFO01";
                cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = AppConfig.IntermediateDbName;

                using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
                int unitCodOrd = reader.GetOrdinal("UnitCod");
                int khtNumOrd = reader.GetOrdinal("KhtNum");
                int pltNumOrd = reader.GetOrdinal("PltNum");
                int kRemarkOrd = reader.GetOrdinal("KRemark");
                int eascom1Ord = reader.GetOrdinal("eascom1");
                int eascom2Ord = reader.GetOrdinal("eascom2");
                int eascom3Ord = reader.GetOrdinal("eascom3");
                int eascom4Ord = reader.GetOrdinal("eascom4");
                int eascom5Ord = reader.GetOrdinal("eascom5");
                int eascom6Ord = reader.GetOrdinal("eascom6");
                int rowidOrd = reader.GetOrdinal("rowid");

                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    tblLinfo01List.Add(new tblLinfo01
                    {
                        UnitCod = reader.IsDBNull(unitCodOrd) ? string.Empty : reader.GetString(unitCodOrd),
                        KhtNum = reader.IsDBNull(khtNumOrd) ? string.Empty : reader.GetString(khtNumOrd),
                        PltNum = reader.IsDBNull(pltNumOrd) ? string.Empty : reader.GetString(pltNumOrd),
                        KRemark = reader.IsDBNull(kRemarkOrd) ? string.Empty : reader.GetString(kRemarkOrd),
                        eascom1 = reader.IsDBNull(eascom1Ord) ? string.Empty : reader.GetString(eascom1Ord),
                        eascom2 = reader.IsDBNull(eascom2Ord) ? string.Empty : reader.GetString(eascom2Ord),
                        eascom3 = reader.IsDBNull(eascom3Ord) ? string.Empty : reader.GetString(eascom3Ord),
                        eascom4 = reader.IsDBNull(eascom4Ord) ? string.Empty : reader.GetString(eascom4Ord),
                        eascom5 = reader.IsDBNull(eascom5Ord) ? string.Empty : reader.GetString(eascom5Ord),
                        eascom6 = reader.IsDBNull(eascom6Ord) ? string.Empty : reader.GetString(eascom6Ord),
                        rowid = reader.IsDBNull(rowidOrd) ? 0 : reader.GetInt32(rowidOrd),
                    });
                }
            }, ct);

            // Fetch tblLinfo02 from DLRS_Migration_Intrm
            await Task.Run(() =>
            {
                using var conn = new SqlConnection(fixedConnectionString);
                conn.Open();
                using var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 300
                };
                cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "SLCTLINFO02";
                cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = AppConfig.IntermediateDbName;

                using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
                int ownNameOrd = reader.GetOrdinal("ownname");
                int addr1Ord = reader.GetOrdinal("addr1");
                int addr2Ord = reader.GetOrdinal("addr2");
                int addr3Ord = reader.GetOrdinal("addr3");
                int addr4Ord = reader.GetOrdinal("addr4");
                int addr5Ord = reader.GetOrdinal("addr5");
                int addr6Ord = reader.GetOrdinal("addr6");
                int addr7Ord = reader.GetOrdinal("addr7");
                int addr8Ord = reader.GetOrdinal("addr8");
                int addr9Ord = reader.GetOrdinal("addr9");
                int addr10Ord = reader.GetOrdinal("addr10");
                int rowidOrd = reader.GetOrdinal("rowid");

                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    tblLinfo02List.Add(new tblLinfo02
                    {
                        OwnName = reader.IsDBNull(ownNameOrd) ? string.Empty : reader.GetString(ownNameOrd),
                        addr1 = reader.IsDBNull(addr1Ord) ? string.Empty : reader.GetString(addr1Ord),
                        addr2 = reader.IsDBNull(addr2Ord) ? string.Empty : reader.GetString(addr2Ord),
                        addr3 = reader.IsDBNull(addr3Ord) ? string.Empty : reader.GetString(addr3Ord),
                        addr4 = reader.IsDBNull(addr4Ord) ? string.Empty : reader.GetString(addr4Ord),
                        addr5 = reader.IsDBNull(addr5Ord) ? string.Empty : reader.GetString(addr5Ord),
                        addr6 = reader.IsDBNull(addr6Ord) ? string.Empty : reader.GetString(addr6Ord),
                        addr7 = reader.IsDBNull(addr7Ord) ? string.Empty : reader.GetString(addr7Ord),
                        addr8 = reader.IsDBNull(addr8Ord) ? string.Empty : reader.GetString(addr8Ord),
                        addr9 = reader.IsDBNull(addr9Ord) ? string.Empty : reader.GetString(addr9Ord),
                        addr10 = reader.IsDBNull(addr10Ord) ? string.Empty : reader.GetString(addr10Ord),
                        rowid = reader.IsDBNull(rowidOrd) ? 0 : reader.GetInt32(rowidOrd),
                    });
                }
            }, ct);

            // Update UI on Step 2
            Dispatcher.Invoke(() =>
            {
                if (tblLinfo01RowCount != null)
                {
                    tblLinfo01RowCount.Visibility = Visibility.Visible;
                    tblLinfo01RowCount.Text = $"tblLInfo01: {tblLinfo01List.Count:N0} records";
                }
                if (tblLinfo02RowCount != null)
                {
                    tblLinfo02RowCount.Visibility = Visibility.Visible;
                    tblLinfo02RowCount.Text = $"tblLInfo02: {tblLinfo02List.Count:N0} records";
                }
            });
        }

        private async Task ConvertFontForAutoAsync(CancellationToken ct)
        {
            int totalToConvert = tblLinfo01List.Count + tblLinfo02List.Count;
            if (totalToConvert == 0) return;

            bool alreadyUnicode = tblLinfo01List.Any(r => ContainsBengaliUnicode(r.KRemark)) ||
                                  tblLinfo02List.Any(r => ContainsBengaliUnicode(r.OwnName));

            if (alreadyUnicode)
            {
                Dispatcher.Invoke(() =>
                {
                    if (txtFullAutoDetail != null)
                        txtFullAutoDetail.Text = "Data is already Unicode. Skipping text re-conversion to prevent character corruption.";
                });
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int processed = 0;
            DateTime lastUi = DateTime.MinValue;

            await Task.Run(() =>
            {
                for (int i = 0; i < tblLinfo01List.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = tblLinfo01List[i];
                    string recId = item.rowid > 0 ? item.rowid.ToString() : (item.UnitCod + ":" + item.KhtNum);

                    if (!string.IsNullOrEmpty(item.KRemark)) item.KRemark = ConvertAndCheckMapping(item.KRemark, "tblLinfo01", "KRemark", recId);
                    if (!string.IsNullOrEmpty(item.eascom1)) item.eascom1 = ConvertAndCheckMapping(item.eascom1, "tblLinfo01", "eascom1", recId);
                    if (!string.IsNullOrEmpty(item.eascom2)) item.eascom2 = ConvertAndCheckMapping(item.eascom2, "tblLinfo01", "eascom2", recId);
                    if (!string.IsNullOrEmpty(item.eascom3)) item.eascom3 = ConvertAndCheckMapping(item.eascom3, "tblLinfo01", "eascom3", recId);
                    if (!string.IsNullOrEmpty(item.eascom4)) item.eascom4 = ConvertAndCheckMapping(item.eascom4, "tblLinfo01", "eascom4", recId);
                    if (!string.IsNullOrEmpty(item.eascom5)) item.eascom5 = ConvertAndCheckMapping(item.eascom5, "tblLinfo01", "eascom5", recId);
                    if (!string.IsNullOrEmpty(item.eascom6)) item.eascom6 = ConvertAndCheckMapping(item.eascom6, "tblLinfo01", "eascom6", recId);

                    processed++;

                    if ((DateTime.UtcNow - lastUi).TotalMilliseconds >= 100 || processed == totalToConvert)
                    {
                        lastUi = DateTime.UtcNow;
                        int curr = processed;
                        double pct = (double)curr / totalToConvert * 100.0;
                        double sec = sw.Elapsed.TotalSeconds;
                        double spd = sec > 0.05 ? curr / sec : 0;
                        double rem = spd > 0 ? (totalToConvert - curr) / spd : 0;

                        Dispatcher.Invoke(() =>
                        {
                            if (pbConversion != null) pbConversion.Value = pct;
                            if (lblConversionPercent != null) lblConversionPercent.Text = $"{pct:F1}%";
                            if (lblConversionCount != null) lblConversionCount.Text = $"{curr:N0} / {totalToConvert:N0} records";
                            if (lblConversionSpeed != null) lblConversionSpeed.Text = $"Speed: {spd:N0} rec/s";
                            if (lblConversionEta != null) lblConversionEta.Text = $"⏱ Remaining: {TimeSpan.FromSeconds(Math.Max(0, rem)):mm\\:ss}";

                            if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 45.0 + (pct * 0.15);
                        });
                    }
                }

                for (int i = 0; i < tblLinfo02List.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = tblLinfo02List[i];
                    string recId = item.rowid > 0 ? item.rowid.ToString() : item.OwnName;

                    if (!string.IsNullOrEmpty(item.OwnName)) item.OwnName = ConvertAndCheckMapping(item.OwnName, "tblLinfo02", "OwnName", recId);
                    if (!string.IsNullOrEmpty(item.addr1)) item.addr1 = ConvertAndCheckMapping(item.addr1, "tblLinfo02", "addr1", recId);
                    if (!string.IsNullOrEmpty(item.addr2)) item.addr2 = ConvertAndCheckMapping(item.addr2, "tblLinfo02", "addr2", recId);
                    if (!string.IsNullOrEmpty(item.addr3)) item.addr3 = ConvertAndCheckMapping(item.addr3, "tblLinfo02", "addr3", recId);
                    if (!string.IsNullOrEmpty(item.addr4)) item.addr4 = ConvertAndCheckMapping(item.addr4, "tblLinfo02", "addr4", recId);
                    if (!string.IsNullOrEmpty(item.addr5)) item.addr5 = ConvertAndCheckMapping(item.addr5, "tblLinfo02", "addr5", recId);
                    if (!string.IsNullOrEmpty(item.addr6)) item.addr6 = ConvertAndCheckMapping(item.addr6, "tblLinfo02", "addr6", recId);
                    if (!string.IsNullOrEmpty(item.addr7)) item.addr7 = ConvertAndCheckMapping(item.addr7, "tblLinfo02", "addr7", recId);
                    if (!string.IsNullOrEmpty(item.addr8)) item.addr8 = ConvertAndCheckMapping(item.addr8, "tblLinfo02", "addr8", recId);
                    if (!string.IsNullOrEmpty(item.addr9)) item.addr9 = ConvertAndCheckMapping(item.addr9, "tblLinfo02", "addr9", recId);
                    if (!string.IsNullOrEmpty(item.addr10)) item.addr10 = ConvertAndCheckMapping(item.addr10, "tblLinfo02", "addr10", recId);

                    processed++;

                    if ((DateTime.UtcNow - lastUi).TotalMilliseconds >= 100 || processed == totalToConvert)
                    {
                        lastUi = DateTime.UtcNow;
                        int curr = processed;
                        double pct = (double)curr / totalToConvert * 100.0;
                        double sec = sw.Elapsed.TotalSeconds;
                        double spd = sec > 0.05 ? curr / sec : 0;
                        double rem = spd > 0 ? (totalToConvert - curr) / spd : 0;

                        Dispatcher.Invoke(() =>
                        {
                            if (pbConversion != null) pbConversion.Value = pct;
                            if (lblConversionPercent != null) lblConversionPercent.Text = $"{pct:F1}%";
                            if (lblConversionCount != null) lblConversionCount.Text = $"{curr:N0} / {totalToConvert:N0} records";
                            if (lblConversionSpeed != null) lblConversionSpeed.Text = $"Speed: {spd:N0} rec/s";
                            if (lblConversionEta != null) lblConversionEta.Text = $"⏱ Remaining: {TimeSpan.FromSeconds(Math.Max(0, rem)):mm\\:ss}";

                            if (pbFullAutoPipeline != null) pbFullAutoPipeline.Value = 45.0 + (pct * 0.15);
                        });
                    }
                }
            }, ct);

            Dispatcher.Invoke(() =>
            {
                if (pbConversion != null) pbConversion.Value = 100;
                if (lblConversionPercent != null) lblConversionPercent.Text = "100%";
                if (lblConversionEta != null) lblConversionEta.Text = "⏱ Finished";
            });
        }

        private async Task CommitUnicodeUpdateForAutoAsync(CancellationToken ct)
        {
            if (tblLinfo01List.Count > 0)
            {
                await UpdateTblLinfo01InBatchesAsync();
            }
            if (tblLinfo02List.Count > 0)
            {
                await UpdateTblLinfo02InBatchesAsync();
            }

            Dispatcher.Invoke(() =>
            {
                if (pbDbUpdate != null) pbDbUpdate.Value = 100;
                if (lblDbUpdatePercent != null) lblDbUpdatePercent.Text = "100%";
                if (dbUpdateSts != null)
                {
                    dbUpdateSts.Text = "✨ All records updated in intermediate database.";
                    dbUpdateSts.Visibility = Visibility.Visible;
                }
            });
        }

        private async Task ExecuteSingleSpStepAsync(string targetCs, MigrationStepConfig cfg, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                using var conn = new SqlConnection(targetCs);
                conn.Open();

                using var cmd = new SqlCommand(cfg.SpName, conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 900 // 15 mins for heavy operations
                };

                cmd.Parameters.Add(new SqlParameter("@ComCod", SqlDbType.NVarChar, 2500) { Value = "4103" });
                cmd.Parameters.Add(new SqlParameter("@ProcID", SqlDbType.NVarChar, 2500) { Value = cfg.ProcId });
                cmd.Parameters.Add(new SqlParameter("@Desc01", SqlDbType.NVarChar, 2500) { Value = AppConfig.IntermediateDbName });

                if (cfg.RequiresRsNumber)
                {
                    cmd.Parameters.Add(new SqlParameter("@Desc02", SqlDbType.NVarChar, 2500) { Value = "201901" });
                }

                cmd.ExecuteNonQuery();
            }, ct);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // ── STEP 1: COPY & STAGE SOURCE DATA TO DLRS_Migration_Intrm ──
        // ─────────────────────────────────────────────────────────────────────────────
        private async void BtnCopyToIntrmDb_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_DbName))
            {
                System.Windows.MessageBox.Show("Please select a source database first.", "Source DB Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_isIntermediateDbReady)
            {
                System.Windows.MessageBox.Show($"Please initialize [{AppConfig.IntermediateDbName}] first.", "Intermediate DB Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (await CheckSelectedDatabaseMigrationStatusAsync())
            {
                PipelineAlertWindow.ShowAlert(
                    this,
                    "Migration Blocked: Unit Already Migrated",
                    "Pre-Check (tblUnitMigrationLog)",
                    $"Unit code(s) in database [{_DbName}] have already been migrated and recorded as COMPLETED in tblUnitMigrationLog.\n\nStaging data is blocked for this unit code to prevent duplicate data insertion.",
                    "Audit log entry with Status = 'COMPLETED' exists in [DLRS_Migration_Intrm].dbo.tblUnitMigrationLog.",
                    allowSkip: false);
                return;
            }

            btnCopyToIntrmDb.IsEnabled = false;
            pbStep1Copy.Visibility = Visibility.Visible;
            txtStep1Status.Visibility = Visibility.Visible;
            txtStep1Status.Foreground = new SolidBrush(MediaColor.FromRgb(56, 189, 248));
            txtStep1Status.Text = $"⏳ Copying and staging data from [{_DbName}] into [{AppConfig.IntermediateDbName}]...";

            _isOperationRunning = true;
            _currentRunningOperationName = $"Step 1: Staging data from [{_DbName}] into [{AppConfig.IntermediateDbName}]";
            _currentOperationCts = new CancellationTokenSource();

            try
            {
                var (hsbCount, l01Count, l02Count) = await IntermediateDbService.StageDataFromSourceAsync(
                    _baseConnectionString, _DbName);

                _isStage2Completed = true; // Step 1 is completed
                pbStep1Copy.Visibility = Visibility.Collapsed;
                txtStep1Status.Visibility = Visibility.Collapsed;

                txtStep1CompletedDetail.Text =
                    $"Data staged successfully in [{AppConfig.IntermediateDbName}]:\n" +
                    $" • tblHalsabek: {hsbCount:N0} rows\n" +
                    $" • tblLinfo01: {l01Count:N0} rows (Unicode-ready KRemark, ROWID)\n" +
                    $" • tblLinfo02: {l02Count:N0} rows (Unicode-ready OwnName/addr, RowId)\n\n" +
                    "Click '➔ Go to Step 2' to start font conversion.";

                borderStep1CompletedBanner.Visibility = Visibility.Visible;
                btnGoToStep2.Visibility = Visibility.Visible;

                // Load tables in intermediate DB for Step 2
                await LoadIntermediateTablesAsync();

                UpdateWorkflowState();

                // Smoothly switch view to Step 2
                ShowStep(2);
            }
            catch (Exception ex)
            {
                pbStep1Copy.Visibility = Visibility.Collapsed;
                txtStep1Status.Text = $"❌ Error copying data: {ex.Message}";
                txtStep1Status.Foreground = System.Windows.Media.Brushes.Tomato;
                btnCopyToIntrmDb.IsEnabled = true;
                System.Windows.MessageBox.Show($"Failed to stage data into [{AppConfig.IntermediateDbName}]:\n\n{ex.Message}", "Error Staging Data", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isOperationRunning = false;
                _currentRunningOperationName = string.Empty;
                if (!_isStage2Completed) btnCopyToIntrmDb.IsEnabled = true;
            }
        }

        private async Task LoadIntermediateTablesAsync()
        {
            try
            {
                await GetTotalRowCount();

                string intrmCs = BuildConnectionString(AppConfig.IntermediateDbName);
                var alreadyConverted = await CheckTablesAlreadyConvertedInDbAsync(intrmCs);

                lstTables.Items.Clear();

                string displayName01 = $"dbo.tblLinfo01 ({tblInf01RowCount})";
                if (alreadyConverted.Contains("dbo.tblLinfo01"))
                    displayName01 += " [✔ Converted to Unicode]";

                string displayName02 = $"dbo.tblLinfo02 ({tblInf02RowCount})";
                if (alreadyConverted.Contains("dbo.tblLinfo02"))
                    displayName02 += " [✔ Converted to Unicode]";

                lstTables.Items.Add(new TableItem { Name = displayName01, IsSelected = true });
                lstTables.Items.Add(new TableItem { Name = displayName02, IsSelected = true });

                if (alreadyConverted.Contains("dbo.tblLinfo01") && alreadyConverted.Contains("dbo.tblLinfo02"))
                {
                    _isStage3Completed = true;
                    updateDatabaseBTN.Content = "✔ Intermediate DB Already in Unicode (Preserved)";
                    borderAlreadyConvertedWarning.Visibility = Visibility.Visible;
                    txtAlreadyConvertedDetail.Text =
                        $"Notice: The intermediate staging tables [{AppConfig.IntermediateDbName}] already contain Bengali Unicode text.\n" +
                        "Re-converting will corrupt characters! Step 2 is marked completed; you can proceed to Step 3.";
                }
                else
                {
                    borderAlreadyConvertedWarning.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        // ------------------------------
        // LOAD TABLES
        // ------------------------------
        private async void Databases_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstDatabases.SelectedItem == null)
                return;

            try
            {
                string dbName = lstDatabases.SelectedItem.ToString()!;

                if (dbName == AppConfig.IntermediateDbName)
                {
                    System.Windows.MessageBox.Show(
                        $"[{AppConfig.IntermediateDbName}] is the intermediate staging database and cannot be selected as the source migration database.\n\nPlease select your legacy source database to proceed.",
                        "Intermediate Database Selected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    lstDatabases.SelectionChanged -= Databases_SelectionChanged;
                    lstDatabases.SelectedItem = null;
                    lstDatabases.SelectionChanged += Databases_SelectionChanged;
                    return;
                }

                if (string.Equals(dbName, _BaseDbName, StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.MessageBox.Show(
                        $"[{_BaseDbName}] is the core destination database and cannot be selected as the source database.\n\nPlease select your legacy source database to proceed.",
                        "Core Target Database Selected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    lstDatabases.SelectionChanged -= Databases_SelectionChanged;
                    lstDatabases.SelectedItem = null;
                    lstDatabases.SelectionChanged += Databases_SelectionChanged;
                    return;
                }

                // Failsafe: Warn user if switching database after operations are already in progress
                if (!string.IsNullOrEmpty(_DbName) && _DbName != dbName && (_isStage2Completed || _isStage3Completed || _isStage4Completed))
                {
                    var confirm = System.Windows.MessageBox.Show(
                        $"Warning: Operations are completed or in progress for database \"{_DbName}\".\n\nSwitching to \"{dbName}\" will reset pipeline states for this session. Do you wish to switch databases?",
                        "Confirm Database Switch",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirm != MessageBoxResult.Yes)
                    {
                        lstDatabases.SelectionChanged -= Databases_SelectionChanged;
                        lstDatabases.SelectedItem = _DbName;
                        lstDatabases.SelectionChanged += Databases_SelectionChanged;
                        return;
                    }
                }

                _DbName = dbName;
                txtActiveDbName.Text = dbName;

                // Reset stage states for newly selected database
                _isStage2Completed = false;
                _isStage3Completed = false;
                _isStage4Completed = false;
                _currentActiveStep = 1;
                updateDatabaseBTN.Content = "💾 Update Intermediate DB";
                borderStep1CompletedBanner.Visibility = Visibility.Collapsed;
                btnGoToStep2.Visibility = Visibility.Collapsed;

                UpdateWorkflowState();

                // Run pre-check: inspect if unit code in selected database is already migrated in tblUnitMigrationLog
                await CheckSelectedDatabaseMigrationStatusAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error");
            }
        }

        private async Task GetTotalRowCount()
        {
            var fixedConnectionString = BuildConnectionString(_BaseDbName);


            using (var conn = new SqlConnection(fixedConnectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    // Required parameters
                    cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500)
                                   .Value = "GETROWCOUNTS";

                    cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = AppConfig.IntermediateDbName;   // your variable

                    using (var adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(tablRowCountData); // DataTable
                    }
                }
            }

            tblInf01RowCount = tablRowCountData.Rows[0][0].ToString();
            tblInf02RowCount = tablRowCountData.Rows[0][1].ToString();

        }


        private async void fontConverterBTN_On_Click(object sender, RoutedEventArgs e)
        {
            if (!_isStage2Completed)
            {
                System.Windows.MessageBox.Show("Step 1 (Copy Source Data to Intermediate Database) must be completed before converting fonts.", "Step 1 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isStage3Completed)
            {
                System.Windows.MessageBox.Show("Failsafe Active: Step 2 (Unicode Conversion & Intermediate DB Update) is already completed.\n\nRe-running this operation is blocked to preserve data integrity.", "Step 2 Locked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int totalToConvert = tblLinfo01List.Count + tblLinfo02List.Count;
            if (totalToConvert == 0)
            {
                System.Windows.MessageBox.Show("No records loaded to convert. Please fetch table data first.");
                return;
            }

            // Guard: Check if loaded records already contain Bengali Unicode
            var alreadyConvertedTables = new List<string>();
            if (tblLinfo01List.Any(r => ContainsBengaliUnicode(r.KRemark)))
                alreadyConvertedTables.Add("dbo.tblLinfo01 (KRemark)");
            if (tblLinfo02List.Any(r => ContainsBengaliUnicode(r.OwnName) || ContainsBengaliUnicode(r.addr1)))
                alreadyConvertedTables.Add("dbo.tblLinfo02 (OwnName / Addresses)");

            if (alreadyConvertedTables.Count > 0)
            {
                if (!UserSession.IsAdmin)
                {
                    System.Windows.MessageBox.Show(
                        $"⛔ Conversion Blocked:\n\n" +
                        $"The following table(s) in database \"{_DbName}\" already contain Bengali Unicode text:\n" +
                        $"• {string.Join("\n• ", alreadyConvertedTables)}\n\n" +
                        "Re-running font conversion on already-converted Unicode text will cause severe character corruption (Mojibake).\n\n" +
                        "This operation has been blocked to protect data integrity.",
                        "Conversion Blocked - Already Converted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Stop);
                    return;
                }
                else
                {
                    var confirm = System.Windows.MessageBox.Show(
                        $"⚠️ DANGER - ALREADY CONVERTED DATA DETECTED:\n\n" +
                        $"The following table(s) in database \"{_DbName}\" already contain Bengali Unicode text:\n" +
                        $"• {string.Join("\n• ", alreadyConvertedTables)}\n\n" +
                        "Re-running font conversion on records that are ALREADY in Unicode will corrupt vowel signs and conjuncts (Yuktakshar), turning Bangla text into illegible Mojibake.\n\n" +
                        "Are you absolutely sure you want to force re-conversion?",
                        "Confirm Re-Conversion",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes)
                        return;
                }
            }

            fontConverterBTN.IsEnabled = false;
            pbConversion.Value = 0;
            lblConversionPercent.Text = "0.0%";
            lblConversionCount.Text = $"0 / {totalToConvert:N0} records";
            lblConversionEta.Text = "⏱ Estimating...";
            lblConversionSpeed.Text = "";
            FontConversionSts.Visibility = Visibility.Collapsed;

            _isOperationRunning = true;
            _currentRunningOperationName = "Step 2: Unicode Font Conversion in memory";
            _currentOperationCts = new CancellationTokenSource();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int processed = 0;
            DateTime lastUiUpdate = DateTime.MinValue;

            void UpdateUi(int currentProcessed, string currentTable)
            {
                double percent = (double)currentProcessed / totalToConvert * 100.0;
                double elapsedSec = sw.Elapsed.TotalSeconds;
                double speed = elapsedSec > 0.05 ? currentProcessed / elapsedSec : 0;
                double remainingSec = speed > 0 ? (totalToConvert - currentProcessed) / speed : 0;
                TimeSpan eta = TimeSpan.FromSeconds(Math.Max(0, remainingSec));

                Dispatcher.Invoke(() =>
                {
                    pbConversion.Value = percent;
                    lblConversionPercent.Text = $"{percent:F1}%";
                    lblConversionCount.Text = $"{currentProcessed:N0} / {totalToConvert:N0} records ({currentTable})";
                    lblConversionSpeed.Text = $"  •  Speed: {speed:N0} rec/s";
                    lblConversionEta.Text = $"⏱ Remaining: {eta:mm\\:ss}";
                });
            }

            // =========================================================================
            // CENTRALIZED PERFORMANCE & CONVERSION PROGRESS CONFIGURATION:
            // - By default, conversion runs in TRUE REAL-TIME (0ms delay, ~10,000+ rec/sec).
            // - If the user checks the Throttle checkbox, a 2ms delay per row is applied.
            // - Both the delay and UI refresh rate can be configured globally in AppConfig.cs.
            // =========================================================================
            int rowDelayMs = (chkConversionDelay?.IsChecked == true)
                ? (AppConfig.ConversionRowDelayMs > 0 ? AppConfig.ConversionRowDelayMs : 2)
                : AppConfig.ConversionRowDelayMs;
            int uiUpdateIntervalMs = Math.Max(20, AppConfig.ConversionUiUpdateIntervalMs);

            try
            {
                await Task.Run(() =>
                {
                    // Convert tblLinfo01
                    if (tblLinfo01List.Count > 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            FontConversionSts1.Text = "🚀 Converting dbo.tblLinfo01...";
                            FontConversionSts1.Visibility = Visibility.Visible;
                        });

                        for (int i = 0; i < tblLinfo01List.Count; i++)
                        {
                            if (_currentOperationCts?.IsCancellationRequested == true)
                                break;

                            var item = tblLinfo01List[i];
                            string recId = item.rowid > 0 ? item.rowid.ToString() : (item.UnitCod + ":" + item.KhtNum);

                            if (!string.IsNullOrEmpty(item.KRemark)) item.KRemark = ConvertAndCheckMapping(item.KRemark, "tblLinfo01", "KRemark", recId);
                            if (!string.IsNullOrEmpty(item.eascom1)) item.eascom1 = ConvertAndCheckMapping(item.eascom1, "tblLinfo01", "eascom1", recId);
                            if (!string.IsNullOrEmpty(item.eascom2)) item.eascom2 = ConvertAndCheckMapping(item.eascom2, "tblLinfo01", "eascom2", recId);
                            if (!string.IsNullOrEmpty(item.eascom3)) item.eascom3 = ConvertAndCheckMapping(item.eascom3, "tblLinfo01", "eascom3", recId);
                            if (!string.IsNullOrEmpty(item.eascom4)) item.eascom4 = ConvertAndCheckMapping(item.eascom4, "tblLinfo01", "eascom4", recId);
                            if (!string.IsNullOrEmpty(item.eascom5)) item.eascom5 = ConvertAndCheckMapping(item.eascom5, "tblLinfo01", "eascom5", recId);
                            if (!string.IsNullOrEmpty(item.eascom6)) item.eascom6 = ConvertAndCheckMapping(item.eascom6, "tblLinfo01", "eascom6", recId);

                            if (rowDelayMs > 0)
                            {
                                System.Threading.Thread.Sleep(rowDelayMs);
                            }

                            processed++;

                            if ((DateTime.UtcNow - lastUiUpdate).TotalMilliseconds > uiUpdateIntervalMs || processed == totalToConvert)
                            {
                                lastUiUpdate = DateTime.UtcNow;
                                UpdateUi(processed, "tblLinfo01");
                            }
                        }

                        Dispatcher.Invoke(() =>
                        {
                            FontConversionSts1.Text = "🎉 dbo.tblLinfo01 completed!";
                        });
                    }

                    // Convert tblLinfo02
                    if (tblLinfo02List.Count > 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            FontConversionSts2.Text = "🚀 Converting dbo.tblLinfo02...";
                            FontConversionSts2.Visibility = Visibility.Visible;
                        });

                        for (int i = 0; i < tblLinfo02List.Count; i++)
                        {
                            if (_currentOperationCts?.IsCancellationRequested == true)
                                break;

                            var item = tblLinfo02List[i];
                            string recId = item.rowid > 0 ? item.rowid.ToString() : item.OwnName;

                            if (!string.IsNullOrEmpty(item.OwnName)) item.OwnName = ConvertAndCheckMapping(item.OwnName, "tblLinfo02", "OwnName", recId);
                            if (!string.IsNullOrEmpty(item.addr1)) item.addr1 = ConvertAndCheckMapping(item.addr1, "tblLinfo02", "addr1", recId);
                            if (!string.IsNullOrEmpty(item.addr2)) item.addr2 = ConvertAndCheckMapping(item.addr2, "tblLinfo02", "addr2", recId);
                            if (!string.IsNullOrEmpty(item.addr3)) item.addr3 = ConvertAndCheckMapping(item.addr3, "tblLinfo02", "addr3", recId);
                            if (!string.IsNullOrEmpty(item.addr4)) item.addr4 = ConvertAndCheckMapping(item.addr4, "tblLinfo02", "addr4", recId);
                            if (!string.IsNullOrEmpty(item.addr5)) item.addr5 = ConvertAndCheckMapping(item.addr5, "tblLinfo02", "addr5", recId);
                            if (!string.IsNullOrEmpty(item.addr6)) item.addr6 = ConvertAndCheckMapping(item.addr6, "tblLinfo02", "addr6", recId);
                            if (!string.IsNullOrEmpty(item.addr7)) item.addr7 = ConvertAndCheckMapping(item.addr7, "tblLinfo02", "addr7", recId);
                            if (!string.IsNullOrEmpty(item.addr8)) item.addr8 = ConvertAndCheckMapping(item.addr8, "tblLinfo02", "addr8", recId);
                            if (!string.IsNullOrEmpty(item.addr9)) item.addr9 = ConvertAndCheckMapping(item.addr9, "tblLinfo02", "addr9", recId);
                            if (!string.IsNullOrEmpty(item.addr10)) item.addr10 = ConvertAndCheckMapping(item.addr10, "tblLinfo02", "addr10", recId);

                            if (rowDelayMs > 0)
                            {
                                System.Threading.Thread.Sleep(rowDelayMs);
                            }

                            processed++;

                            if ((DateTime.UtcNow - lastUiUpdate).TotalMilliseconds > uiUpdateIntervalMs || processed == totalToConvert)
                            {
                                lastUiUpdate = DateTime.UtcNow;
                                UpdateUi(processed, "tblLinfo02");
                            }
                        }

                        Dispatcher.Invoke(() =>
                        {
                            FontConversionSts2.Text = "🎉 dbo.tblLinfo02 completed!";
                        });
                    }
                });

                sw.Stop();

                if (_currentOperationCts?.IsCancellationRequested == true)
                {
                    FontConversionSts.Text = "⚠️ Conversion was cancelled by user.";
                    FontConversionSts.Visibility = Visibility.Visible;
                    return;
                }

                pbConversion.Value = 100;
                lblConversionPercent.Text = "100.0%";
                lblConversionCount.Text = $"{totalToConvert:N0} / {totalToConvert:N0} records";
                lblConversionEta.Text = $"⏱ Finished in {sw.Elapsed:mm\\:ss}";
                lblConversionSpeed.Text = $"  •  Avg: {(totalToConvert / Math.Max(0.1, sw.Elapsed.TotalSeconds)):N0} rec/s";

                int warnCount = PipelineExecutionLogger.WarningCount;
                if (warnCount > 0)
                {
                    FontConversionSts.Text = $"⚠️ Converted with {warnCount} character mapping notice(s). Click [📋 Logs] in the top bar to inspect.";
                    FontConversionSts.Foreground = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                }
                else
                {
                    FontConversionSts.Text = "✅ All records converted to Unicode with 100% mapping accuracy! Proceed to update database.";
                    FontConversionSts.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
                FontConversionSts.Visibility = Visibility.Visible;

                updateDatabaseBTN.IsEnabled = true;
            }
            finally
            {
                _isOperationRunning = false;
                _currentRunningOperationName = string.Empty;
                fontConverterBTN.IsEnabled = true;
            }
        }

        private async void updateDatabaseBTN_On_Click(object sender, RoutedEventArgs e)
        {
            if (!_isStage2Completed)
            {
                System.Windows.MessageBox.Show("Step 1 (Copy Source Data to Intermediate Database) must be completed before updating intermediate database.", "Step 1 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isStage3Completed)
            {
                System.Windows.MessageBox.Show("Failsafe Active: Intermediate database has already been updated with Unicode.\n\nRe-running this operation is blocked to preserve data consistency.", "Step 2 Locked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            updateDatabaseBTN.IsEnabled = false;

            int batchSize = AppConfig.BatchChunkSize;
            int totalBatches1 = (int)Math.Ceiling((double)tblLinfo01List.Count / batchSize);
            int totalBatches2 = (int)Math.Ceiling((double)tblLinfo02List.Count / batchSize);
            int totalBatches  = totalBatches1 + totalBatches2;

            pbDbUpdate.Value = 0;
            lblDbUpdatePercent.Text = "0%";
            lblDbUpdateBatchInfo.Text = $"Updating {totalBatches} batch(es)...";

            _isOperationRunning = true;
            _currentRunningOperationName = "Step 2: Updating intermediate database with Unicode records";
            _currentOperationCts = new CancellationTokenSource();

            try
            {
                if (tblLinfo01List.Count > 0)
                {
                    await UpdateTblLinfo01InBatchesAsync();
                }

                if (_currentOperationCts?.IsCancellationRequested == true)
                {
                    dbUpdateSts.Text = "⚠️ Database update was cancelled by user.";
                    dbUpdateSts.Visibility = Visibility.Visible;
                    return;
                }

                if (tblLinfo02List.Count > 0)
                {
                    await UpdateTblLinfo02InBatchesAsync();
                }

                if (_currentOperationCts?.IsCancellationRequested == true)
                {
                    dbUpdateSts.Text = "⚠️ Database update was cancelled by user.";
                    dbUpdateSts.Visibility = Visibility.Visible;
                    return;
                }

                pbDbUpdate.Value = 100;
                lblDbUpdatePercent.Text = "100%";
                lblDbUpdateBatchInfo.Text = $"All {totalBatches} batches completed.";

                dbUpdateSts.Text = "✨ 🎉 All records updated — database is good to go! Ready for Stage 4 (Migration). 🎯";
                dbUpdateSts.Visibility = Visibility.Visible;

                _isStage3Completed = true;
                UpdateWorkflowState();
                updateDatabaseBTN.IsEnabled = false;
                updateDatabaseBTN.Content = "✔ Intermediate DB Updated with Unicode (Locked)";
                fontConverterBTN.IsEnabled = false;
                fetchDataForSelectedTables.IsEnabled = false;
                if (btnGoToStep3 != null) btnGoToStep3.Visibility = Visibility.Visible;
                ShowStep(3); // Smoothly transition to Step 3 (Migration)
            }
            finally
            {
                _isOperationRunning = false;
                _currentRunningOperationName = string.Empty;
                if (!_isStage3Completed) updateDatabaseBTN.IsEnabled = true;
            }
        }

        private async Task UpdateTblLinfo01InBatchesAsync()
        {
            var fixedConnectionString = BuildConnectionString(_BaseDbName);
            int batchSize = AppConfig.BatchChunkSize;
            int batchNumber = 0;

            try
            {
                var batches = tblLinfo01List.Batch(batchSize).ToList();
                int totalBatches = batches.Count;

                foreach (var batch in batches)
                {
                    if (_currentOperationCts?.IsCancellationRequested == true)
                    {
                        tblLinfo01_Update_Status.Text = "⚠️ Update cancelled by user.";
                        break;
                    }

                    batchNumber++;

                    tblLinfo01_Update_Status.Text =
                        $"Updating tblLinfo01: batch {batchNumber} of {totalBatches}...";
                    tblLinfo01_Update_Status.Visibility = Visibility.Visible;

                    // IMPORTANT: only send THIS batch
                    var dataList = new List<object> { batch };

                    string jsonstring = UtilityFunction.ListToJsonString(dataList, "tbl_tblLinfo01");

                    string? result = await Task.Run(() =>
                    {
                        using (var conn = new SqlConnection(fixedConnectionString))
                        {
                            conn.Open();
                            using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "UPDTTBLINF01";
                                cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = AppConfig.IntermediateDbName;
                                cmd.Parameters.Add("@Djson1", SqlDbType.NVarChar, -1).Value = jsonstring;

                                return cmd.ExecuteScalar()?.ToString();
                            }
                        }
                    });

                    if (AppConfig.BatchUpdateDelayMs > 0)
                        await Task.Delay(AppConfig.BatchUpdateDelayMs);
                }

                tblLinfo01_Update_Status.Text = $"tblLinfo01 updated successfully ({totalBatches} batches).";
            }
            catch (Exception ex)
            {
                tblLinfo01_Update_Status.Text = $"Unexpected error: {ex.Message}";
                return;
            }
        }


        private async Task UpdateTblLinfo02InBatchesAsync()
        {
            var fixedConnectionString = BuildConnectionString(_BaseDbName);
            int batchSize = AppConfig.BatchChunkSize;
            int batchNumber = 0;

            try
            {
                var batches = tblLinfo02List.Batch(batchSize).ToList();
                int totalBatches = batches.Count;

                foreach (var batch in batches)
                {
                    if (_currentOperationCts?.IsCancellationRequested == true)
                    {
                        tblLinfo02_Update_Status.Text = "⚠️ Update cancelled by user.";
                        break;
                    }

                    batchNumber++;

                    tblLinfo02_Update_Status.Text = $"Updating tblLinfo02: batch {batchNumber} of {totalBatches}...";
                    tblLinfo02_Update_Status.Visibility = Visibility.Visible;

                    // IMPORTANT: only send THIS batch
                    var dataList = new List<object> { batch };

                    string jsonstring = UtilityFunction.ListToJsonString(dataList, "tbl_tblLinfo02");

                    string? result = await Task.Run(() =>
                    {
                        using (var conn = new SqlConnection(fixedConnectionString))
                        {
                            conn.Open();
                            using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "UPDTTBLINF02";
                                cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = AppConfig.IntermediateDbName;
                                cmd.Parameters.Add("@Djson1", SqlDbType.NVarChar, -1).Value = jsonstring;

                                return cmd.ExecuteScalar()?.ToString();
                            }
                        }
                    });

                    if (AppConfig.BatchUpdateDelayMs > 0)
                        await Task.Delay(AppConfig.BatchUpdateDelayMs);
                }

                tblLinfo02_Update_Status.Text = $"tblLinfo02 updated successfully ({totalBatches} batches).";
            }
            catch (Exception ex)
            {
                tblLinfo02_Update_Status.Text = $"Unexpected error: {ex.Message}";
                return;
            }
        }


        private async void ShowSelectedTables_Click(object sender, RoutedEventArgs e)
        {
            if (!_isStage2Completed)
            {
                System.Windows.MessageBox.Show("Step 1 (Copy Source Data to Intermediate Database) must be completed before fetching table data.", "Step 1 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isStage3Completed)
            {
                System.Windows.MessageBox.Show("Failsafe Active: Step 2 (Unicode Conversion & Intermediate DB Update) is already completed.\n\nFetching tables again is disabled to prevent overwriting migrated data.", "Step 2 Locked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedTables = lstTables.Items.Cast<TableItem>().Where(t => t.IsSelected).Select(t => t.Name).ToList();

            if (selectedTables.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select at least one table to fetch.", "No Table Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            fetchDataForSelectedTables.IsEnabled = false;

            _isOperationRunning = true;
            _currentRunningOperationName = "Step 2: Fetching staging tables from intermediate database";
            _currentOperationCts = new CancellationTokenSource();

            try
            {
                // ── Clear previous run data to prevent overlap / duplication ──
                tblLinfo01List.Clear();
                tblLinfo02List.Clear();
                tablRowCountData.Clear();

                DataFetchingStatusFortblLinfo01.Visibility = Visibility.Collapsed;
                tblLinfo01RowCount.Visibility = Visibility.Collapsed;
                DataFetchingStatusFortblLinfo02.Visibility = Visibility.Collapsed;
                tblLinfo02RowCount.Visibility = Visibility.Collapsed;

                string firstTableName = selectedTables
                    .FirstOrDefault(t => t.Contains("tblLInfo01", StringComparison.OrdinalIgnoreCase))
                    ?? string.Empty;

                string secondTableName = selectedTables
                    .FirstOrDefault(t => t.Contains("tblLInfo02", StringComparison.OrdinalIgnoreCase))
                    ?? string.Empty;

                var fixedConnectionString = BuildConnectionString(_BaseDbName);

                if (!string.IsNullOrEmpty(firstTableName))
                {
                    DataFetchingStatus.Visibility = Visibility.Visible;
                    DataFetchingStatus.Foreground = System.Windows.Media.Brushes.Gray;
                    DataFetchingStatus.Text = $"Starting fetching data for {firstTableName.Split(' ')[0]}...";

                    await Task.Run(() =>
                    {
                        using (var conn = new SqlConnection(fixedConnectionString))
                        {
                            conn.Open();
                            using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.CommandTimeout = 300;
                                cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "SLCTLINFO01";
                                cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = AppConfig.IntermediateDbName;

                                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                                {
                                    int unitCodOrd = reader.GetOrdinal("UnitCod");
                                    int khtNumOrd = reader.GetOrdinal("KhtNum");
                                    int pltNumOrd = reader.GetOrdinal("PltNum");
                                    int kRemarkOrd = reader.GetOrdinal("KRemark");
                                    int eascom1Ord = reader.GetOrdinal("eascom1");
                                    int eascom2Ord = reader.GetOrdinal("eascom2");
                                    int eascom3Ord = reader.GetOrdinal("eascom3");
                                    int eascom4Ord = reader.GetOrdinal("eascom4");
                                    int eascom5Ord = reader.GetOrdinal("eascom5");
                                    int eascom6Ord = reader.GetOrdinal("eascom6");
                                    int rowidOrd = reader.GetOrdinal("rowid");

                                    while (reader.Read())
                                    {
                                        tblLinfo01List.Add(new tblLinfo01
                                        {
                                            UnitCod = reader.IsDBNull(unitCodOrd) ? string.Empty : reader.GetString(unitCodOrd),
                                            KhtNum = reader.IsDBNull(khtNumOrd) ? string.Empty : reader.GetString(khtNumOrd),
                                            PltNum = reader.IsDBNull(pltNumOrd) ? string.Empty : reader.GetString(pltNumOrd),
                                            KRemark = reader.IsDBNull(kRemarkOrd) ? string.Empty : reader.GetString(kRemarkOrd),
                                            eascom1 = reader.IsDBNull(eascom1Ord) ? string.Empty : reader.GetString(eascom1Ord),
                                            eascom2 = reader.IsDBNull(eascom2Ord) ? string.Empty : reader.GetString(eascom2Ord),
                                            eascom3 = reader.IsDBNull(eascom3Ord) ? string.Empty : reader.GetString(eascom3Ord),
                                            eascom4 = reader.IsDBNull(eascom4Ord) ? string.Empty : reader.GetString(eascom4Ord),
                                            eascom5 = reader.IsDBNull(eascom5Ord) ? string.Empty : reader.GetString(eascom5Ord),
                                            eascom6 = reader.IsDBNull(eascom6Ord) ? string.Empty : reader.GetString(eascom6Ord),
                                            rowid = reader.IsDBNull(rowidOrd) ? 0 : reader.GetInt32(rowidOrd),
                                        });
                                    }
                                }
                            }
                        }
                    });

                    DataFetchingStatus.Visibility = Visibility.Collapsed;
                    DataFetchingStatusFortblLinfo01.Visibility = Visibility.Visible;
                    DataFetchingStatusFortblLinfo01.Text = "Data retrieval for tblLInfo01 completed successfully.";
                    tblLinfo01RowCount.Visibility = Visibility.Visible;
                    tblLinfo01RowCount.Text = $"tblLInfo01 — Total Records: {tblLinfo01List.Count:N0}";
                }

                if (!string.IsNullOrEmpty(secondTableName))
                {
                    DataFetchingStatus2.Visibility = Visibility.Visible;
                    DataFetchingStatus2.Text = $"Starting fetching data for {secondTableName.Split(' ')[0]}...";

                    await Task.Run(() =>
                    {
                        using (var conn = new SqlConnection(fixedConnectionString))
                        {
                            conn.Open();
                            using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.CommandTimeout = 300;
                                cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "SLCTLINFO02";
                                cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = AppConfig.IntermediateDbName;

                                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                                {
                                    int ownNameOrd = reader.GetOrdinal("OwnName");
                                    int addr1Ord = reader.GetOrdinal("addr1");
                                    int addr2Ord = reader.GetOrdinal("addr2");
                                    int addr3Ord = reader.GetOrdinal("addr3");
                                    int addr4Ord = reader.GetOrdinal("addr4");
                                    int addr5Ord = reader.GetOrdinal("addr5");
                                    int addr6Ord = reader.GetOrdinal("addr6");
                                    int addr7Ord = reader.GetOrdinal("addr7");
                                    int addr8Ord = reader.GetOrdinal("addr8");
                                    int addr9Ord = reader.GetOrdinal("addr9");
                                    int addr10Ord = reader.GetOrdinal("addr10");
                                    int rowidOrd = reader.GetOrdinal("rowid");

                                    while (reader.Read())
                                    {
                                        tblLinfo02List.Add(new tblLinfo02
                                        {
                                            OwnName = reader.IsDBNull(ownNameOrd) ? string.Empty : reader.GetString(ownNameOrd),
                                            addr1 = reader.IsDBNull(addr1Ord) ? string.Empty : reader.GetString(addr1Ord),
                                            addr2 = reader.IsDBNull(addr2Ord) ? string.Empty : reader.GetString(addr2Ord),
                                            addr3 = reader.IsDBNull(addr3Ord) ? string.Empty : reader.GetString(addr3Ord),
                                            addr4 = reader.IsDBNull(addr4Ord) ? string.Empty : reader.GetString(addr4Ord),
                                            addr5 = reader.IsDBNull(addr5Ord) ? string.Empty : reader.GetString(addr5Ord),
                                            addr6 = reader.IsDBNull(addr6Ord) ? string.Empty : reader.GetString(addr6Ord),
                                            addr7 = reader.IsDBNull(addr7Ord) ? string.Empty : reader.GetString(addr7Ord),
                                            addr8 = reader.IsDBNull(addr8Ord) ? string.Empty : reader.GetString(addr8Ord),
                                            addr9 = reader.IsDBNull(addr9Ord) ? string.Empty : reader.GetString(addr9Ord),
                                            addr10 = reader.IsDBNull(addr10Ord) ? string.Empty : reader.GetString(addr10Ord),
                                            rowid = reader.IsDBNull(rowidOrd) ? 0 : reader.GetInt32(rowidOrd),
                                        });
                                    }
                                }
                            }
                        }
                    });

                    DataFetchingStatus2.Visibility = Visibility.Collapsed;
                    DataFetchingStatusFortblLinfo02.Visibility = Visibility.Visible;
                    DataFetchingStatusFortblLinfo02.Text = "Data retrieval for tblLInfo02 completed successfully.";
                    tblLinfo02RowCount.Visibility = Visibility.Visible;
                    tblLinfo02RowCount.Text = $"tblLInfo02 — Total Records: {tblLinfo02List.Count:N0}";
                }

                // Check if fetched records already contain Bengali Unicode
                var inMemoryConverted = new List<string>();
                if (tblLinfo01List.Any(r => ContainsBengaliUnicode(r.KRemark)))
                    inMemoryConverted.Add("dbo.tblLinfo01 (KRemark)");
                if (tblLinfo02List.Any(r => ContainsBengaliUnicode(r.OwnName) || ContainsBengaliUnicode(r.addr1)))
                    inMemoryConverted.Add("dbo.tblLinfo02 (OwnName / Addresses)");

                if (inMemoryConverted.Count > 0)
                {
                    borderAlreadyConvertedWarning.Visibility = Visibility.Visible;
                    txtAlreadyConvertedDetail.Text =
                        $"Notice: The fetched table(s) [{string.Join(", ", inMemoryConverted)}] ALREADY contain Bengali Unicode text.\n" +
                        "Re-running font conversion will distort characters and cause Mojibake!";

                    fontConversionPermission.Visibility = Visibility.Visible;
                    fontConversionPermission.Foreground = System.Windows.Media.Brushes.Tomato;
                    fontConversionPermission.Text = $"⚠️ Notice: {string.Join(" and ", inMemoryConverted)} already formatted in Unicode.";

                    System.Windows.MessageBox.Show(
                        $"⚠️ Warning: Converted Data Detected!\n\n" +
                        $"The following table(s) retrieved from intermediate database (source: \"{_DbName}\") already contain Bengali Unicode text:\n" +
                        $"• {string.Join("\n• ", inMemoryConverted)}\n\n" +
                        "Running font conversion on already-converted Unicode text will cause irreversible font distortion (Mojibake).\n\n" +
                        (UserSession.IsAdmin
                            ? "As Admin, you can review this or force re-conversion if strictly required."
                            : "For safety, font conversion will be blocked for standard users."),
                        "Already Converted Tables Detected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    borderAlreadyConvertedWarning.Visibility = Visibility.Collapsed;
                    fontConversionPermission.Visibility = Visibility.Visible;
                    fontConversionPermission.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
                    fontConversionPermission.Text = "✅ All set! You can now start the font conversion operation.";
                }

                fontConverterBTN.IsEnabled = true;
            }
            catch (Exception ex)
            {
                DataFetchingStatus.Visibility = Visibility.Visible;
                DataFetchingStatus.Foreground = System.Windows.Media.Brushes.Tomato;
                DataFetchingStatus.Text = $"❌ Error: {ex.Message}";
                System.Windows.MessageBox.Show($"Failed to fetch table data from intermediate database:\n\n{ex.Message}", "Fetch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isOperationRunning = false;
                _currentRunningOperationName = string.Empty;
                fetchDataForSelectedTables.IsEnabled = true;
            }
        }

        // ------------------------------
        // OPEN DB PREPARATION WIZARD
        // ------------------------------
        private async void BtnPrepareDb_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_DbName))
            {
                System.Windows.MessageBox.Show("Please select a database first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string cs = BuildConnectionString(_DbName);
            var wizard = new DbPrepWizardWindow(cs, _DbName)
            {
                Owner = this
            };
            wizard.ShowDialog();

            if (wizard.IsPreparationCompleted)
            {
                _isStage2Completed = true;
                UpdateWorkflowState();
                System.Windows.MessageBox.Show("Stage 2 preparation completed successfully! Stage 3 (Font Conversion) is now unlocked.", "Stage 2 Completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Re-verify in case user ran all steps
                bool isPrepared = await CheckDatabasePreparedAsync(cs);
                _isStage2Completed = isPrepared;
                UpdateWorkflowState();
            }
        }

        // ------------------------------
        // OPEN DATA MIGRATION WIZARD
        // ------------------------------
        private async void BtnDataMigrationWizard_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_DbName))
            {
                System.Windows.MessageBox.Show("Please select a database first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!_isStage3Completed)
            {
                System.Windows.MessageBox.Show("Please complete Stage 3 (Unicode Font Conversion & Database Update) before proceeding to Stage 4 (Data Migration).", "Stage 3 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetCs = BuildConnectionString(_BaseDbName);
            // Staged and Unicode-converted data resides in DLRS_Migration_Intrm
            var wizard = new DataMigrationWizardWindow(targetCs, AppConfig.IntermediateDbName)
            {
                Owner = this
            };

            bool logged = false;
            wizard.MigrationCompleted += async () =>
            {
                _isStage4Completed = true;
                UpdateWorkflowState();
                if (!logged && !string.IsNullOrEmpty(_DbName) && !string.IsNullOrEmpty(_baseConnectionString))
                {
                    logged = true;
                    await IntermediateDbService.LogCompletedUnitsAsync(_baseConnectionString, _DbName, "COMPLETED");
                }
            };

            wizard.ShowDialog();

            if (wizard.IsMigrationCompleted)
            {
                _isStage4Completed = true;
                UpdateWorkflowState();
                if (!logged && !string.IsNullOrEmpty(_DbName) && !string.IsNullOrEmpty(_baseConnectionString))
                {
                    logged = true;
                    await IntermediateDbService.LogCompletedUnitsAsync(_baseConnectionString, _DbName, "COMPLETED");
                }
            }
            else
            {
                // Re-verify in database in case steps were executed
                string cs = BuildConnectionString(_DbName);
                bool isDone = await CheckMigrationAlreadyCompletedInDbAsync(cs);
                if (isDone)
                {
                    _isStage4Completed = true;
                    UpdateWorkflowState();
                    if (!logged && !string.IsNullOrEmpty(_DbName) && !string.IsNullOrEmpty(_baseConnectionString))
                    {
                        logged = true;
                        await IntermediateDbService.LogCompletedUnitsAsync(_baseConnectionString, _DbName, "COMPLETED");
                    }
                }
            }
        }

        // ------------------------------
        // VIEW MIGRATED UNIT CODES REPORT
        // ------------------------------
        private void BtnViewMigratedUnits_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_DbName)) return;

            string sourceCs = BuildConnectionString(_DbName);
            string targetCs = BuildConnectionString(_BaseDbName);

            var dialog = new MigratedUnitsSummaryDialog(_DbName, sourceCs, targetCs)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

    }

    class TableItem
    {
        public string Name { get; set; }
        public bool IsSelected { get; set; }
    }


    public static class EnumerableExtensions
    {
        public static IEnumerable<List<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
        {
            List<T> batch = new(batchSize);

            foreach (var item in source)
            {
                batch.Add(item);

                if (batch.Count == batchSize)
                {
                    yield return batch;
                    batch = new List<T>(batchSize);
                }
            }

            if (batch.Count > 0)
                yield return batch;
        }
    }



}