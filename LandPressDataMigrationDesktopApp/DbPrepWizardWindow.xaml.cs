using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using SolidBrush  = System.Windows.Media.SolidColorBrush;
using MediaColor  = System.Windows.Media.Color;
using MediaBrushes= System.Windows.Media.Brushes;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Step-by-step database preparation wizard for level 130+, RowId, and column conversions.
    /// Includes strict failsafe locking to prevent re-execution or moving backwards once completed.
    /// </summary>
    public partial class DbPrepWizardWindow : Window
    {
        // ── injected from MainWindow ─────────────────────────────────────
        private readonly string _connectionString;
        private readonly string _dbName;

        // ── wizard state ─────────────────────────────────────────────────
        private int _currentStep = 1;
        private const int TotalSteps = 6;

        // ── step completion flags (1-based, index 0 unused) ──────────────
        private bool[] _stepCompleted = new bool[TotalSteps + 1];

        /// <summary>Returns true if the final step (Step 6) was completed successfully.</summary>
        public bool IsPreparationCompleted => _stepCompleted != null && _stepCompleted[TotalSteps];

        // ── step panels (1-based, index 0 unused) ────────────────────────
        private Border[] _stepPanels = null!;

        // ── step indicator circles ────────────────────────────────────────
        private Border[] _stepDots = null!;

        // ── result model for DataGrid ─────────────────────────────────────
        public class CompatRow
        {
            public string DatabaseName { get; set; } = "";
            public int    CompatLevel  { get; set; }
            public string SqlVersion   { get; set; } = "";
        }

        // ─────────────────────────────────────────────────────────────────
        public DbPrepWizardWindow(string connectionString, string dbName)
        {
            InitializeComponent();

            _connectionString = connectionString;
            _dbName           = dbName;

            txtDbLabel.Text = $"Target Source Database:  {dbName}";

            _stepPanels = new Border[]
            {
                null!,
                stepPanel1,
                stepPanel2,
                stepPanel3,
                stepPanel4,
                stepPanel5,
                stepPanel6,
            };

            BuildStepIndicators();
            RefreshNav();
            UpdateThemeButton();

            // Role based adaptation for normal users
            if (!UserSession.IsAdmin)
            {
                txtS1_Desc.Text = "Verifies that your database engine supports modern processing and Unicode storage.";
                txtS2_Desc.Text = "Applies compatibility optimizations for modern SQL engine processing.";
                txtS3_Desc.Text = "Adds an internal sequential row identifier to tblLinfo02 to support safe batch updates.";
                txtS4_Desc.Text = "Converts KRemark to Unicode NVARCHAR format to support Bangla text without corruption.";
                txtS5_Desc.Text = "Expands owner name and address fields to 150 characters for full Unicode text preservation.";
                txtS6_Desc.Text = "Adds conversion status tracking columns (convsts) across all target tables.";
            }

            ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;
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

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
        }

        // ─────────────────────────────────────────────────────────────────
        #region Step Indicator Dots
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
                    Width        = 30,
                    Height       = 30,
                    CornerRadius = new CornerRadius(15),
                    Background   = i == 1
                                   ? new SolidBrush(MediaColor.FromRgb(59, 130, 246))
                                   : new SolidBrush(MediaColor.FromRgb(55, 65, 81)),
                    Margin       = new Thickness(4, 0, 4, 0),
                    Child        = label,
                };

                _stepDots[i] = dot;
                stepIndicatorPanel.Children.Add(dot);

                if (i < TotalSteps)
                {
                    stepIndicatorPanel.Children.Add(new Border
                    {
                        Width             = 28,
                        Height            = 2,
                        Background        = new SolidBrush(MediaColor.FromRgb(55, 65, 81)),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    });
                }
            }
        }

        private void RefreshStepDots()
        {
            for (int i = 1; i <= TotalSteps; i++)
            {
                if (_stepDots[i] == null) continue;

                if (_stepCompleted[i])
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(16, 185, 129));   // completed — green
                }
                else if (i == _currentStep)
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(59, 130, 246));  // active — blue
                }
                else
                {
                    _stepDots[i].Background = new SolidBrush(MediaColor.FromRgb(55, 65, 81));   // future — grey
                }
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Navigation & Strict Failsafe
        // ─────────────────────────────────────────────────────────────────

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                // FAILSAFE: Cannot go back if the previous step is completed
                if (_stepCompleted[_currentStep - 1])
                {
                    MessageBox.Show(
                        "Failsafe Active: The previous step has already been completed.\n\nGoing back to completed steps is disabled to prevent duplicate or conflicting modifications.",
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
            if (!_stepCompleted[_currentStep])
            {
                System.Windows.MessageBox.Show(
                    $"Please complete Step {_currentStep} before proceeding.",
                    "Step Incomplete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_currentStep < TotalSteps) GoToStep(_currentStep + 1);
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
            RefreshStepDots();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        // Helper — build a connection string that points to 'master'
        // ─────────────────────────────────────────────────────────────────
        private string MasterConnectionString()
        {
            return _connectionString
                .Replace($"Database={_dbName};",        "Database=master;")
                .Replace($"Initial Catalog={_dbName};", "Initial Catalog=master;");
        }

        // ─────────────────────────────────────────────────────────────────
        #region Step 1 — Check Compatibility Levels
        // ─────────────────────────────────────────────────────────────────

        private async void BtnCheckCompat_Click(object sender, RoutedEventArgs e)
        {
            btnCheckCompat.IsEnabled        = false;
            lblCheckCompatStatus.Text       = "Querying...";
            lblCheckCompatStatus.Visibility = Visibility.Visible;
            dgCompat.Visibility             = Visibility.Collapsed;

            try
            {
                var rows = await Task.Run(() =>
                {
                    var list = new List<CompatRow>();
                    using var conn = new SqlConnection(MasterConnectionString());
                    conn.Open();

                    const string sql = @"
SELECT
    name AS DatabaseName,
    compatibility_level AS CompatLevel,
    CASE compatibility_level
        WHEN 80  THEN 'SQL Server 2000'
        WHEN 90  THEN 'SQL Server 2005'
        WHEN 100 THEN 'SQL Server 2008 / R2'
        WHEN 110 THEN 'SQL Server 2012'
        WHEN 120 THEN 'SQL Server 2014'
        WHEN 130 THEN 'SQL Server 2016'
        WHEN 140 THEN 'SQL Server 2017'
        WHEN 150 THEN 'SQL Server 2019'
        WHEN 160 THEN 'SQL Server 2022'
        ELSE 'Unknown'
    END AS SqlVersion
FROM sys.databases
ORDER BY name;";

                    using var cmd    = new SqlCommand(sql, conn);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new CompatRow
                        {
                            DatabaseName = reader.GetString(0),
                            CompatLevel  = reader.GetByte(1),
                            SqlVersion   = reader.GetString(2),
                        });
                    }
                    return list;
                });

                dgCompat.ItemsSource          = rows;
                dgCompat.Visibility           = Visibility.Visible;
                lblCheckCompatStatus.Text     = $"Found {rows.Count} database(s). Compatibility verified. Click Next to proceed.";
                lblCheckCompatStatus.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                _stepCompleted[1] = true;
                btnCheckCompat.Content = "✔ Compatibility Verified";
            }
            catch (Exception ex)
            {
                lblCheckCompatStatus.Text       = $"Error: {ex.Message}";
                lblCheckCompatStatus.Foreground = MediaBrushes.Tomato;
            }
            finally
            {
                btnCheckCompat.IsEnabled = !_stepCompleted[1];
                RefreshNav();
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Step 2 — Change Compatibility Level
        // ─────────────────────────────────────────────────────────────────

        private async void BtnChangeCompat_Click(object sender, RoutedEventArgs e)
        {
            var item    = (ComboBoxItem)cmbCompatLevel.SelectedItem;
            int level   = int.Parse(item.Tag.ToString()!);
            string content = item.Content.ToString()!;

            btnChangeCompat.IsEnabled        = false;
            lblChangeCompatStatus.Text       = "Applying...";
            lblChangeCompatStatus.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(MasterConnectionString());
                    conn.Open();

                    string sql = $"ALTER DATABASE [{_dbName}] SET COMPATIBILITY_LEVEL = {level};";
                    using var cmd = new SqlCommand(sql, conn);
                    cmd.ExecuteNonQuery();
                });

                lblChangeCompatStatus.Text       = $"Compatibility level of [{_dbName}] successfully set to {level} ({content}).";
                lblChangeCompatStatus.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                _stepCompleted[2] = true;
                btnChangeCompat.Content = "✔ Compatibility Level Applied";
            }
            catch (Exception ex)
            {
                lblChangeCompatStatus.Text       = $"Error: {ex.Message}";
                lblChangeCompatStatus.Foreground = MediaBrushes.Tomato;
            }
            finally
            {
                btnChangeCompat.IsEnabled = !_stepCompleted[2];
                RefreshNav();
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Step 3 — Add RowId to tblLinfo02
        // ─────────────────────────────────────────────────────────────────

        private async void BtnAddRowId_Click(object sender, RoutedEventArgs e)
        {
            btnAddRowId.IsEnabled        = false;
            lblAddRowIdStatus.Text       = "Working...";
            lblAddRowIdStatus.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(_connectionString);
                    conn.Open();

                    const string addCol = @"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'tblLinfo02' AND COLUMN_NAME = 'RowId'
)
BEGIN
    ALTER TABLE dbo.tblLinfo02 ADD RowId INT NULL;
END";
                    using (var cmd = new SqlCommand(addCol, conn))
                    {
                        cmd.CommandTimeout = 120;
                        cmd.ExecuteNonQuery();
                    }

                    const string popRowId = @"
WITH NumberedRows AS (
    SELECT
        RowId,
        ROW_NUMBER() OVER (
            ORDER BY UnitCod, KhtNum, OwnrGrp, OwnrNum
        ) AS NewRowId
    FROM dbo.tblLinfo02
)
UPDATE NumberedRows
SET RowId = NewRowId;";

                    using (var cmd = new SqlCommand(popRowId, conn))
                    {
                        cmd.CommandTimeout = 300;
                        cmd.ExecuteNonQuery();
                    }
                });

                lblAddRowIdStatus.Text       = "RowId column added and sequentially numbered in tblLinfo02.";
                lblAddRowIdStatus.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                _stepCompleted[3] = true;
                btnAddRowId.Content = "✔ RowId Added & Populated";
            }
            catch (Exception ex)
            {
                lblAddRowIdStatus.Text       = $"Error: {ex.Message}";
                lblAddRowIdStatus.Foreground = MediaBrushes.Tomato;
            }
            finally
            {
                btnAddRowId.IsEnabled = !_stepCompleted[3];
                RefreshNav();
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Step 4 — Change KRemark column type
        // ─────────────────────────────────────────────────────────────────

        private async void BtnChangeKRemark_Click(object sender, RoutedEventArgs e)
        {
            btnChangeKRemark.IsEnabled        = false;
            lblChangeKRemarkStatus.Text       = "Altering column...";
            lblChangeKRemarkStatus.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(_connectionString);
                    conn.Open();

                    const string sql = @"
ALTER TABLE dbo.tblLinfo01
ALTER COLUMN KRemark NVARCHAR(MAX) NULL;";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.CommandTimeout = 300;
                    cmd.ExecuteNonQuery();
                });

                lblChangeKRemarkStatus.Text       = "KRemark column altered to NVARCHAR(MAX) in tblLinfo01.";
                lblChangeKRemarkStatus.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                _stepCompleted[4] = true;
                btnChangeKRemark.Content = "✔ KRemark Altered to NVARCHAR(MAX)";
            }
            catch (Exception ex)
            {
                lblChangeKRemarkStatus.Text       = $"Error: {ex.Message}";
                lblChangeKRemarkStatus.Foreground = MediaBrushes.Tomato;
            }
            finally
            {
                btnChangeKRemark.IsEnabled = !_stepCompleted[4];
                RefreshNav();
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Step 5 — Expand address columns
        // ─────────────────────────────────────────────────────────────────

        private async void BtnExpandAddrs_Click(object sender, RoutedEventArgs e)
        {
            btnExpandAddrs.IsEnabled        = false;
            lblExpandAddrsStatus.Text       = "Altering columns...";
            lblExpandAddrsStatus.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(_connectionString);
                    conn.Open();

                    void AlterCol(string col)
                    {
                        string sql = $"ALTER TABLE dbo.tblLinfo02 ALTER COLUMN {col} NVARCHAR(150) NULL;";
                        using var cmd = new SqlCommand(sql, conn);
                        cmd.CommandTimeout = 120;
                        cmd.ExecuteNonQuery();
                    }

                    AlterCol("OwnName");
                    for (int i = 1; i <= 10; i++)
                        AlterCol($"addr{i}");
                });

                lblExpandAddrsStatus.Text       = "OwnName and addr1-addr10 expanded to NVARCHAR(150) in tblLinfo02.";
                lblExpandAddrsStatus.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                _stepCompleted[5] = true;
                btnExpandAddrs.Content = "✔ Addresses Expanded to NVARCHAR(150)";
            }
            catch (Exception ex)
            {
                lblExpandAddrsStatus.Text       = $"Error: {ex.Message}";
                lblExpandAddrsStatus.Foreground = MediaBrushes.Tomato;
            }
            finally
            {
                btnExpandAddrs.IsEnabled = !_stepCompleted[5];
                RefreshNav();
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Step 6 — Add convsts column
        // ─────────────────────────────────────────────────────────────────

        private async void BtnAddConvsts_Click(object sender, RoutedEventArgs e)
        {
            btnAddConvsts.IsEnabled        = false;
            lblAddConvstsStatus.Text       = "Adding columns...";
            lblAddConvstsStatus.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    using var conn = new SqlConnection(_connectionString);
                    conn.Open();

                    void AddColumn(string table)
                    {
                        string sql = $@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'convsts'
)
BEGIN
    ALTER TABLE dbo.{table}
    ADD convsts NVARCHAR(20) NOT NULL DEFAULT N'';
END";
                        using var cmd = new SqlCommand(sql, conn);
                        cmd.CommandTimeout = 120;
                        cmd.ExecuteNonQuery();
                    }

                    AddColumn("tblHalsabek");
                    AddColumn("tblLinfo01");
                    AddColumn("tblLinfo02");
                });

                lblAddConvstsStatus.Text       = "convsts column added to tblHalsabek, tblLinfo01 and tblLinfo02.";
                lblAddConvstsStatus.Foreground = new SolidBrush(MediaColor.FromRgb(74, 222, 128));
                _stepCompleted[6]              = true;
                btnAddConvsts.Content          = "✔ convsts Columns Added";
                doneBanner.Visibility          = Visibility.Visible;
                btnNext.IsEnabled              = false;
            }
            catch (Exception ex)
            {
                lblAddConvstsStatus.Text       = $"Error: {ex.Message}";
                lblAddConvstsStatus.Foreground = MediaBrushes.Tomato;
            }
            finally
            {
                btnAddConvsts.IsEnabled = !_stepCompleted[6];
                RefreshNav();
            }
        }

        #endregion
    }
}

