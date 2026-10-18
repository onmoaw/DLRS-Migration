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
        private bool _isStage2Completed = false;
        private bool _isStage3Completed = false;
        private bool _isStage4Completed = false;

        public MainWindow()
        {
            InitializeComponent();
            cmbAuth.SelectedIndex = 0;
            cmbAuth.SelectionChanged += Auth_SelectionChanged;

            UpdateThemeButton();
            ApplyRoleView();

            ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;

            UpdateWorkflowState();
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

        private void ApplyRoleView()
        {
            if (UserSession.IsAdmin)
            {
                txtUserRoleBadge.Text = "👤 Admin (Full Access)";
                borderUserRoleBadge.Background = new SolidBrush(MediaColor.FromRgb(6, 95, 70));

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
                txtUserRoleBadge.Text = "👤 User (Standard Mode)";
                borderUserRoleBadge.Background = new SolidBrush(MediaColor.FromRgb(30, 58, 138));

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

                if (txtStage2Desc != null)
                    txtStage2Desc.Text = "Verifies and prepares database compatibility, Unicode columns, and row tracking.";

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
        private void Connect_Click(object sender, RoutedEventArgs e)
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
                conn.Open();

                LoadDatabases(conn);

                // Show green success feedback
                txtConnectError.Foreground = System.Windows.Media.Brushes.LimeGreen;
                txtConnectError.Text       = "✔ Connected successfully.";
                txtConnectError.Visibility = Visibility.Visible;

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
        }

        // ─────────────────────────────────────────────────────────────────────────────
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
                ORDER BY name";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
                lstDatabases.Items.Add(reader.GetString(0));
        }

        // ── WORKFLOW STATE MANAGEMENT & SEQUENTIAL ENFORCEMENT ──
        private void UpdateWorkflowState()
        {
            bool hasDb = !string.IsNullOrEmpty(_DbName);
            bool isDark = ThemeManager.CurrentTheme == AppTheme.Dark;

            var inactiveBorder = new SolidBrush(isDark ? MediaColor.FromRgb(51, 65, 85) : MediaColor.FromRgb(203, 213, 225));
            var inactiveBadge  = new SolidBrush(isDark ? MediaColor.FromRgb(100, 116, 139) : MediaColor.FromRgb(148, 163, 184));
            var inactiveText   = new SolidBrush(isDark ? MediaColor.FromRgb(148, 163, 184) : MediaColor.FromRgb(71, 85, 105));

            // ── STEP 1 CHIP ──
            if (hasDb)
            {
                chipStep1.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                borderChip1Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                txtChip1Num.Text = "✔";
                txtChip1Status.Text = $"Selected: {_DbName}";
                txtChip1Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
            }
            else
            {
                chipStep1.BorderBrush = new SolidBrush(MediaColor.FromRgb(59, 130, 246));
                borderChip1Badge.Background = new SolidBrush(MediaColor.FromRgb(59, 130, 246));
                txtChip1Num.Text = "1";
                txtChip1Status.Text = "Connect & select source DB";
                txtChip1Status.Foreground = inactiveText;
            }

            // ── STEP 2 CHIP & STAGE 2 CARD ──
            btnPrepareDb.IsEnabled = hasDb;
            if (!hasDb)
            {
                chipStep2.BorderBrush = inactiveBorder;
                borderChip2Badge.Background = inactiveBadge;
                txtChip2Num.Text = "2";
                txtChip2Status.Text = "Waiting for DB Selection";
                txtChip2Status.Foreground = inactiveText;

                badgeStage2.Background = inactiveBadge;
                txtStage2Badge.Text = "PENDING";
                txtStage2StatusTitle.Text = "Select a database to verify";
                txtStage2StatusTitle.Foreground = inactiveText;
                txtStage2StatusDetail.Text = "Prerequisites must be completed before Stage 3 (Font Conversion) will unlock.";
            }
            else if (_isStage2Completed)
            {
                chipStep2.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                borderChip2Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                txtChip2Num.Text = "✔";
                txtChip2Status.Text = "✔ Preparation Completed";
                txtChip2Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));

                badgeStage2.Background = new SolidBrush(MediaColor.FromRgb(6, 78, 59));
                txtStage2Badge.Text = "VERIFIED";
                txtStage2StatusTitle.Text = "✔ Database Prepared & Verified";
                txtStage2StatusTitle.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                txtStage2StatusDetail.Text = "Compatibility level 130+, NVARCHAR types, RowId, and convsts are active.";
            }
            else
            {
                chipStep2.BorderBrush = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                borderChip2Badge.Background = new SolidBrush(MediaColor.FromRgb(245, 158, 11));
                txtChip2Num.Text = "2";
                txtChip2Status.Text = "⚠ Action Required";
                txtChip2Status.Foreground = new SolidBrush(MediaColor.FromRgb(252, 211, 77));

                badgeStage2.Background = new SolidBrush(MediaColor.FromRgb(69, 26, 3));
                txtStage2Badge.Text = "ACTION REQUIRED";
                txtStage2StatusTitle.Text = "⚠ Preparation Needed Before Conversion";
                txtStage2StatusTitle.Foreground = new SolidBrush(MediaColor.FromRgb(251, 191, 36));
                txtStage2StatusDetail.Text = "Click 'Open Database Preparation Wizard' below to run the 6 preparation steps.";
            }

            // ── STEP 3 CHIP & STAGE 3 CARD ──
            if (!_isStage2Completed)
            {
                borderStage3LockBanner.Visibility = Visibility.Visible;
                borderStage3UnlockedBanner.Visibility = Visibility.Collapsed;

                lstTables.IsEnabled = false;
                fetchDataForSelectedTables.IsEnabled = false;
                fontConverterBTN.IsEnabled = false;
                updateDatabaseBTN.IsEnabled = false;

                chipStep3.BorderBrush = inactiveBorder;
                borderChip3Badge.Background = inactiveBadge;
                txtChip3Num.Text = "3";
                txtChip3Status.Text = "🔒 Locked (Needs Step 2)";
                txtChip3Status.Foreground = inactiveText;
            }
            else
            {
                borderStage3LockBanner.Visibility = Visibility.Collapsed;
                borderStage3UnlockedBanner.Visibility = Visibility.Visible;

                if (_isStage3Completed)
                {
                    // FAILSAFE: Lock Stage 3 once completed to prevent re-conversion or data corruption
                    lstTables.IsEnabled = false;
                    fetchDataForSelectedTables.IsEnabled = false;
                    fontConverterBTN.IsEnabled = false;
                    updateDatabaseBTN.IsEnabled = false;
                    updateDatabaseBTN.Content = "✔ Database Updated with Unicode (Locked)";

                    chipStep3.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                    borderChip3Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                    txtChip3Num.Text = "✔";
                    txtChip3Status.Text = "✔ Converted & Updated";
                    txtChip3Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
                else
                {
                    lstTables.IsEnabled = true;
                    fetchDataForSelectedTables.IsEnabled = true;

                    chipStep3.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                    borderChip3Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                    txtChip3Num.Text = "3";
                    txtChip3Status.Text = "Ready to Convert";
                    txtChip3Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));
                }
            }

            // ── STEP 4 CHIP & STAGE 4 CARD ──
            if (_isStage4Completed)
            {
                borderStage4LockBanner.Visibility = Visibility.Collapsed;
                borderStage4UnlockedBanner.Visibility = Visibility.Collapsed;
                if (borderStage4CompletedBanner != null)
                    borderStage4CompletedBanner.Visibility = Visibility.Visible;
                btnDataMigrationWizard.IsEnabled = true;

                if (btnViewMigratedUnits != null)
                    btnViewMigratedUnits.Visibility = Visibility.Visible;

                // Step 4 Chip in green completed state
                chipStep4.BorderBrush = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                borderChip4Badge.Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));
                txtChip4Num.Text = "✔";
                txtChip4Status.Text = "✔ Migration Completed";
                txtChip4Status.Foreground = new SolidBrush(MediaColor.FromRgb(52, 211, 153));

                if (txtBtnDataMigrationWizard != null)
                    txtBtnDataMigrationWizard.Text = "✔ Migration Completed (Review Pipeline)";

                btnDataMigrationWizard.Background = new System.Windows.Media.LinearGradientBrush(
                    MediaColor.FromRgb(16, 185, 129),
                    MediaColor.FromRgb(5, 150, 105),
                    new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
            }
            else if (!_isStage3Completed)
            {
                borderStage4LockBanner.Visibility = Visibility.Visible;
                borderStage4UnlockedBanner.Visibility = Visibility.Collapsed;
                if (borderStage4CompletedBanner != null)
                    borderStage4CompletedBanner.Visibility = Visibility.Collapsed;
                btnDataMigrationWizard.IsEnabled = false;

                if (btnViewMigratedUnits != null)
                    btnViewMigratedUnits.Visibility = Visibility.Collapsed;

                chipStep4.BorderBrush = inactiveBorder;
                borderChip4Badge.Background = inactiveBadge;
                txtChip4Num.Text = "4";
                txtChip4Status.Text = "🔒 Locked (Needs Step 3)";
                txtChip4Status.Foreground = inactiveText;

                if (txtBtnDataMigrationWizard != null)
                    txtBtnDataMigrationWizard.Text = "🚀 Launch Data Migration Wizard";

                btnDataMigrationWizard.Background = new System.Windows.Media.LinearGradientBrush(
                    MediaColor.FromRgb(139, 92, 246),
                    MediaColor.FromRgb(217, 70, 239),
                    new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
            }
            else
            {
                borderStage4LockBanner.Visibility = Visibility.Collapsed;
                borderStage4UnlockedBanner.Visibility = Visibility.Visible;
                if (borderStage4CompletedBanner != null)
                    borderStage4CompletedBanner.Visibility = Visibility.Collapsed;
                btnDataMigrationWizard.IsEnabled = true;

                if (btnViewMigratedUnits != null)
                    btnViewMigratedUnits.Visibility = Visibility.Collapsed;

                chipStep4.BorderBrush = new SolidBrush(MediaColor.FromRgb(139, 92, 246));
                borderChip4Badge.Background = new SolidBrush(MediaColor.FromRgb(139, 92, 246));
                txtChip4Num.Text = "4";
                txtChip4Status.Text = "🚀 Ready for Migration";
                txtChip4Status.Foreground = new SolidBrush(MediaColor.FromRgb(196, 181, 253));

                if (txtBtnDataMigrationWizard != null)
                    txtBtnDataMigrationWizard.Text = "🚀 Launch Data Migration Wizard";

                btnDataMigrationWizard.Background = new System.Windows.Media.LinearGradientBrush(
                    MediaColor.FromRgb(139, 92, 246),
                    MediaColor.FromRgb(217, 70, 239),
                    new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
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

        // ------------------------------
        // LOAD TABLES
        // ------------------------------
        private async void Databases_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstDatabases.SelectedItem == null)
                return;

            try
            {
                string dbName = lstDatabases.SelectedItem.ToString();

                // Failsafe: Warn user if switching database after Stage 2, 3, or 4 is already completed
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
                updateDatabaseBTN.Content = "💾 Update Database";
                UpdateWorkflowState();

                // Show the Prepare Database & Migration Wizard buttons
                btnPrepareDb.Visibility = Visibility.Visible;
                btnDataMigrationWizard.Visibility = Visibility.Visible;

                string cs = BuildConnectionString(dbName);

                // Run verification check for Stage 2
                txtStage2StatusTitle.Text = "Verifying database preparation...";
                bool isPrepared = await CheckDatabasePreparedAsync(cs);
                _isStage2Completed = isPrepared;

                // Run check to identify whether tables are already converted to Unicode
                var alreadyConvertedTables = await CheckTablesAlreadyConvertedInDbAsync(cs);
                bool allTargetTablesConverted = alreadyConvertedTables.Contains("dbo.tblLinfo01") &&
                                                 alreadyConvertedTables.Contains("dbo.tblLinfo02");

                if (alreadyConvertedTables.Count > 0)
                {
                    borderAlreadyConvertedWarning.Visibility = Visibility.Visible;
                    txtAlreadyConvertedDetail.Text =
                        $"Notice: The following table(s) in \"{dbName}\" already contain Bengali Unicode text:\n" +
                        $"• {string.Join("\n• ", alreadyConvertedTables)}\n\n" +
                        "Re-running font conversion will distort characters and cause Mojibake." +
                        (allTargetTablesConverted
                            ? " Stage 3 is marked as completed; you can proceed directly to Stage 4 (Migration Pipeline)."
                            : " Please do not re-convert the tables marked with [✔ Converted].");

                    if (allTargetTablesConverted)
                    {
                        _isStage3Completed = true;
                        updateDatabaseBTN.Content = "✔ Database Already in Unicode (Preserved)";
                    }
                }
                else
                {
                    borderAlreadyConvertedWarning.Visibility = Visibility.Collapsed;
                }

                // Run check to identify whether Stage 4 (Data Migration) was already completed
                bool isMigrationDone = await CheckMigrationAlreadyCompletedInDbAsync(cs);
                if (isMigrationDone)
                {
                    _isStage4Completed = true;
                }

                UpdateWorkflowState();

                using var conn = new SqlConnection(cs);
                conn.Open();

                lstTables.Items.Clear();

                string sql = @"
                    SELECT TABLE_SCHEMA + '.' + TABLE_NAME
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_TYPE = 'BASE TABLE'
                    ORDER BY TABLE_SCHEMA, TABLE_NAME";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();

                await GetTotalRowCount();

                // Excluding the first table (Halsabek) with the help of the following boolean flag
                bool isFirst = true;
                while (reader.Read())
                {
                    if (isFirst)
                    {
                        isFirst = false;
                        continue;
                    }

                    string tableName = reader.GetString(0);
                    bool isTblInfo01 = tableName.Contains("tblLInfo01", StringComparison.OrdinalIgnoreCase);
                    bool isTblInfo02 = tableName.Contains("tblLInfo02", StringComparison.OrdinalIgnoreCase);
                    string displayName = tableName;
                    if (isTblInfo01)
                    {
                        displayName = $"{tableName} ({tblInf01RowCount})";
                        if (alreadyConvertedTables.Contains("dbo.tblLinfo01"))
                            displayName += " [✔ Converted to Unicode]";
                    }
                    else if (isTblInfo02)
                    {
                        displayName = $"{tableName} ({tblInf02RowCount})";
                        if (alreadyConvertedTables.Contains("dbo.tblLinfo02"))
                            displayName += " [✔ Converted to Unicode]";
                    }
                    lstTables.Items.Add(new TableItem
                    {
                        Name = displayName,
                        IsSelected = false
                    });
                }

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

                    cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200)
                                   .Value = _DbName;   // your variable

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
                System.Windows.MessageBox.Show("Stage 2 (Database Preparation & Compatibility) must be completed before converting fonts.", "Stage 2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isStage3Completed)
            {
                System.Windows.MessageBox.Show("Failsafe Active: Stage 3 (Unicode Conversion & Database Update) is already completed and committed.\n\nRe-running this operation is blocked to preserve data integrity.", "Stage 3 Locked", MessageBoxButton.OK, MessageBoxImage.Information);
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
            // CONVERSION THROTTLE DELAY CONFIGURATION:
            // - Admin User: Can toggle delay ON/OFF via UI checkbox (chkConversionDelay).
            // - Standard User: Delay is ALWAYS forced ON (2ms per row) to prevent CPU starvation.
            // Note: The delay is applied in MULTIPLE places (2 locations):
            //   Location 1: tblLinfo01 loop below (line ~668)
            //   Location 2: tblLinfo02 loop below (line ~716)
            // =========================================================================
            bool applyDelay = !UserSession.IsAdmin || (chkConversionDelay?.IsChecked == true);

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
                        var item = tblLinfo01List[i];
                        if (!string.IsNullOrEmpty(item.KRemark))
                        {
                            item.KRemark = ConvertToAdarshalipi(item.KRemark);
                        }

                        // DELAY LOCATION 1: Per-row throttling delay for tblLinfo01 (2ms)
                        if (applyDelay)
                        {
                            System.Threading.Thread.Sleep(2);
                        }

                        processed++;

                        if ((DateTime.UtcNow - lastUiUpdate).TotalMilliseconds > 100 || processed == totalToConvert)
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
                        var item = tblLinfo02List[i];
                        if (!string.IsNullOrEmpty(item.OwnName)) item.OwnName = ConvertToAdarshalipi(item.OwnName);
                        if (!string.IsNullOrEmpty(item.addr1)) item.addr1 = ConvertToAdarshalipi(item.addr1);
                        if (!string.IsNullOrEmpty(item.addr2)) item.addr2 = ConvertToAdarshalipi(item.addr2);
                        if (!string.IsNullOrEmpty(item.addr3)) item.addr3 = ConvertToAdarshalipi(item.addr3);
                        if (!string.IsNullOrEmpty(item.addr4)) item.addr4 = ConvertToAdarshalipi(item.addr4);
                        if (!string.IsNullOrEmpty(item.addr5)) item.addr5 = ConvertToAdarshalipi(item.addr5);
                        if (!string.IsNullOrEmpty(item.addr6)) item.addr6 = ConvertToAdarshalipi(item.addr6);
                        if (!string.IsNullOrEmpty(item.addr7)) item.addr7 = ConvertToAdarshalipi(item.addr7);
                        if (!string.IsNullOrEmpty(item.addr8)) item.addr8 = ConvertToAdarshalipi(item.addr8);
                        if (!string.IsNullOrEmpty(item.addr9)) item.addr9 = ConvertToAdarshalipi(item.addr9);
                        if (!string.IsNullOrEmpty(item.addr10)) item.addr10 = ConvertToAdarshalipi(item.addr10);

                        // DELAY LOCATION 2: Per-row throttling delay for tblLinfo02 (2ms)
                        if (applyDelay)
                        {
                            System.Threading.Thread.Sleep(2);
                        }

                        processed++;

                        if ((DateTime.UtcNow - lastUiUpdate).TotalMilliseconds > 100 || processed == totalToConvert)
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

            pbConversion.Value = 100;
            lblConversionPercent.Text = "100.0%";
            lblConversionCount.Text = $"{totalToConvert:N0} / {totalToConvert:N0} records";
            lblConversionEta.Text = $"⏱ Finished in {sw.Elapsed:mm\\:ss}";
            lblConversionSpeed.Text = $"  •  Avg: {(totalToConvert / Math.Max(0.1, sw.Elapsed.TotalSeconds)):N0} rec/s";

            FontConversionSts.Text = "✅ All records converted to Unicode! Proceed to update database.";
            FontConversionSts.Visibility = Visibility.Visible;

            updateDatabaseBTN.IsEnabled = true;
            fontConverterBTN.IsEnabled = true;
        }

        private async void updateDatabaseBTN_On_Click(object sender, RoutedEventArgs e)
        {
            if (!_isStage2Completed)
            {
                System.Windows.MessageBox.Show("Stage 2 (Database Preparation & Compatibility) must be completed before updating database.", "Stage 2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isStage3Completed)
            {
                System.Windows.MessageBox.Show("Failsafe Active: Database has already been updated with Unicode.\n\nRe-running this operation is blocked to preserve data consistency.", "Operation Locked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            updateDatabaseBTN.IsEnabled = false;

            int totalBatches1 = (int)Math.Ceiling((double)tblLinfo01List.Count / 50000);
            int totalBatches2 = (int)Math.Ceiling((double)tblLinfo02List.Count / 50000);
            int totalBatches  = totalBatches1 + totalBatches2;

            pbDbUpdate.Value = 0;
            lblDbUpdatePercent.Text = "0%";
            lblDbUpdateBatchInfo.Text = $"Updating {totalBatches} batch(es)...";

            if (tblLinfo01List.Count > 0)
            {
                await UpdateTblLinfo01InBatchesAsync();
            }

            if (tblLinfo02List.Count > 0)
            {
                await UpdateTblLinfo02InBatchesAsync();
            }

            pbDbUpdate.Value = 100;
            lblDbUpdatePercent.Text = "100%";
            lblDbUpdateBatchInfo.Text = $"All {totalBatches} batches completed.";

            dbUpdateSts.Text = "✨ 🎉 All records updated — database is good to go! Ready for Stage 4 (Migration). 🎯";
            dbUpdateSts.Visibility = Visibility.Visible;

            _isStage3Completed = true;
            UpdateWorkflowState();
            updateDatabaseBTN.IsEnabled = false;
            updateDatabaseBTN.Content = "✔ Database Updated with Unicode (Locked)";
            fontConverterBTN.IsEnabled = false;
            fetchDataForSelectedTables.IsEnabled = false;
        }

        private async Task UpdateTblLinfo01InBatchesAsync()
        {

            var fixedConnectionString = BuildConnectionString(_BaseDbName);
            const int batchSize = 50_000;
            int batchNumber = 0;

            try
            {
                var batches = tblLinfo01List.Batch(batchSize).ToList();
                int totalBatches = batches.Count;

                foreach (var batch in batches)
                {
                    batchNumber++;

                    tblLinfo01_Update_Status.Text =
                        $"Updating tblLinfo01: batch {batchNumber} of {totalBatches}...";
                    tblLinfo01_Update_Status.Visibility = Visibility.Visible;

                    // IMPORTANT: only send THIS batch
                    var dataList = new List<object> { batch };

                    string jsonstring = UtilityFunction.ListToJsonString(dataList, "tbl_tblLinfo01");


                    //await Task.Run(() =>
                    //{
                    //    using (var conn = new SqlConnection(fixedConnectionString))
                    //    {
                    //        conn.Open();
                    //        using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                    //        {
                    //            cmd.CommandType = CommandType.StoredProcedure;
                    //            cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "UPDATE_KREMARK_TBLLINFO_01";
                    //            cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = _DbName;
                    //            cmd.Parameters.Add("@Djson1", SqlDbType.NVarChar, -1).Value = jsonstring;
                    //            //using (var adapter = new SqlDataAdapter(cmd))
                    //            //{
                    //            //    adapter.Fill(table1Data);
                    //            //}
                    //            //string? result = cmd.ExecuteScalar()?.ToString();
                    //            return cmd.ExecuteScalar()?.ToString();
                    //        }
                    //    }
                    //});



                    string? result = await Task.Run(() =>
                    {
                        using (var conn = new SqlConnection(fixedConnectionString))
                        {
                            conn.Open();
                            using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "UPDTTBLINF01";
                                cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = _DbName;
                                cmd.Parameters.Add("@Djson1", SqlDbType.NVarChar, -1).Value = jsonstring;

                                return cmd.ExecuteScalar()?.ToString();
                            }
                        }
                    });

                }

                tblLinfo01_Update_Status.Text = $"tblLinfo01 updated successfully ({totalBatches} batches).";
                //await Task.Delay(3);
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
            const int batchSize = 50_000;
            int batchNumber = 0;

            try
            {
                var batches = tblLinfo02List.Batch(batchSize).ToList();
                int totalBatches = batches.Count;

                foreach (var batch in batches)
                {
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
                                cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = _DbName;
                                cmd.Parameters.Add("@Djson1", SqlDbType.NVarChar, -1).Value = jsonstring;

                                return cmd.ExecuteScalar()?.ToString();
                            }
                        }
                    });

                }

                tblLinfo02_Update_Status.Text = $"tblLinfo02 updated successfully ({totalBatches} batches).";
                await Task.Delay(3);
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
                System.Windows.MessageBox.Show("Stage 2 (Database Preparation & Compatibility) must be completed before fetching and converting table data.", "Stage 2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isStage3Completed)
            {
                System.Windows.MessageBox.Show("Failsafe Active: Stage 3 (Unicode Conversion & Database Update) is already completed and locked.\n\nFetching tables again is disabled to prevent overwriting migrated data.", "Stage 3 Locked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedTables = lstTables.Items .Cast<TableItem>() .Where(t => t.IsSelected) .Select(t => t.Name) .ToList();


            if (selectedTables.Count == 0)
            {
                System.Windows.MessageBox.Show("Plz select at least one table");
                return;
            }

            // ── Clear previous run data to prevent overlap / duplication ──
            tblLinfo01List.Clear();
            tblLinfo02List.Clear();
            tablRowCountData.Clear();



            string firstTableName = selectedTables
                .FirstOrDefault(t => t.Contains("tblLInfo01", StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;

            string secondTableName = selectedTables
                .FirstOrDefault(t => t.Contains("tblLInfo02", StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;



            //****************************************************************************************

            var fixedConnectionString = BuildConnectionString(_BaseDbName);


            if(!string.IsNullOrEmpty(firstTableName))
            {
                DataFetchingStatus.Visibility = Visibility.Visible;
                DataFetchingStatus.Text = $"Starting Fetching Data for {firstTableName.Split(' ')[0]}";

                //using (var conn = new SqlConnection(fixedConnectionString))
                //{
                //    conn.Open();

                //    using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                //    {
                //        cmd.CommandType = CommandType.StoredProcedure;

                //        // Required parameters
                //        cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500)
                //                       .Value = "SLCTLINFO01";

                //        cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200)
                //                       .Value = _DbName;   // your variable

                //        using (var adapter = new SqlDataAdapter(cmd))
                //        {
                //            adapter.Fill(table1Data); // DataTable
                //        }
                //    }
                //}



                //await Task.Run(() =>
                //{
                //    using (var conn = new SqlConnection(fixedConnectionString))
                //    {
                //        conn.Open();
                //        using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                //        {
                //            cmd.CommandType = CommandType.StoredProcedure;
                //            cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "SLCTLINFO01";
                //            cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = _DbName;
                //            using (var adapter = new SqlDataAdapter(cmd))
                //            {
                //                adapter.Fill(table1Data);
                //            }
                //        }
                //    }
                //});



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
                            cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = _DbName;

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
                tblLinfo01RowCount.Text = $"tblLInfo01 — Total Records: {tblInf01RowCount}";

            }



            if(!string.IsNullOrEmpty(secondTableName))
            {

                DataFetchingStatus2.Visibility = Visibility.Visible;

                DataFetchingStatus2.Text = $"Starting Fetching Data for {secondTableName.Split(' ')[0]}";


                //using (var conn = new SqlConnection(fixedConnectionString))
                //{
                //    conn.Open();

                //    using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                //    {
                //        cmd.CommandType = CommandType.StoredProcedure;

                //        // Required parameters
                //        cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500)
                //                       .Value = "SLCTLINFO02";

                //        cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200)
                //                       .Value = _DbName;   // your variable

                //        using (var adapter = new SqlDataAdapter(cmd))
                //        {
                //            adapter.Fill(table2Data); // DataTable
                //        }
                //    }
                //}

                //await Task.Run(() =>
                //{
                //    using (var conn = new SqlConnection(fixedConnectionString))
                //    {
                //        conn.Open();
                //        using (var cmd = new SqlCommand("SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK", conn))
                //        {
                //            cmd.CommandType = CommandType.StoredProcedure;
                //            cmd.Parameters.Add("@ProcID", SqlDbType.NVarChar, 2500).Value = "SLCTLINFO02";
                //            cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = _DbName;
                //            using (var adapter = new SqlDataAdapter(cmd))
                //            {
                //                adapter.Fill(table2Data);
                //            }
                //        }
                //    }
                //});



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
                            cmd.Parameters.Add("@Desc01", SqlDbType.NVarChar, 200).Value = _DbName;

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
                tblLinfo02RowCount.Text = $"tblLInfo02 — Total Records: {tblInf02RowCount}";

            }

            //string jsonString1 = JsonConvert.SerializeObject(table1Data);
            //string jsonString2 = JsonConvert.SerializeObject(table2Data);


            //*****************************************************************


            //tblLinfo01List = JsonConvert.DeserializeObject<List<tblLinfo01>>(jsonString1);
            //tblLinfo02List = JsonConvert.DeserializeObject<List<tblLinfo02>>(jsonString2);


            //tblLinfo01List = JsonConvert.DeserializeObject<List<tblLinfo01>>(JsonConvert.SerializeObject(table1Data));
            //tblLinfo02List = JsonConvert.DeserializeObject<List<tblLinfo02>>(JsonConvert.SerializeObject(table2Data));



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
                    $"The following table(s) retrieved from database \"{_DbName}\" already contain Bengali Unicode text:\n" +
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
            var wizard = new DataMigrationWizardWindow(targetCs, _DbName)
            {
                Owner = this
            };

            wizard.MigrationCompleted += () =>
            {
                _isStage4Completed = true;
                UpdateWorkflowState();
            };

            wizard.ShowDialog();

            if (wizard.IsMigrationCompleted)
            {
                _isStage4Completed = true;
                UpdateWorkflowState();
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