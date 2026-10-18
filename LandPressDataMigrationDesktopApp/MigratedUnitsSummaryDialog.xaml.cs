using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace LandPressDataMigrationDesktopApp
{
    public partial class MigratedUnitsSummaryDialog : Window
    {
        private readonly string _sourceDbName;
        private readonly string _sourceConnectionString;
        private readonly string? _targetConnectionString;

        public class MigratedUnitSummaryItem
        {
            public int SlNo { get; set; }
            public string UnitCode { get; set; } = string.Empty;
            public string MouzaName { get; set; } = "-";
            public int TotalKhatians { get; set; }
            public int TotalPlots { get; set; }
            public int TotalOwners { get; set; }
            public string FormattedKhatians => TotalKhatians.ToString("N0");
            public string FormattedPlots => TotalPlots.ToString("N0");
            public string FormattedOwners => TotalOwners.ToString("N0");
            public string Status { get; set; } = "✔ Migrated";
        }

        public MigratedUnitsSummaryDialog(string sourceDbName, string sourceConnectionString, string? targetConnectionString = null)
        {
            InitializeComponent();

            _sourceDbName = sourceDbName;
            _sourceConnectionString = sourceConnectionString;
            _targetConnectionString = targetConnectionString;

            txtDbInfo.Text = $"Source Database: [{sourceDbName}]  •  Core Target: [DLRSLSINF_4103PRSDB]";

            UpdateThemeButton();
            ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;

            Loaded += async (s, e) => await LoadDataAsync();
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

        private async Task LoadDataAsync()
        {
            txtLoading.Visibility = Visibility.Visible;
            dgMigratedUnits.Visibility = Visibility.Collapsed;

            try
            {
                var items = await Task.Run(() => FetchMigratedUnits());

                dgMigratedUnits.ItemsSource = items;

                int totalUnits = items.Count;
                int totalKhatians = items.Sum(x => x.TotalKhatians);
                int totalPlots = items.Sum(x => x.TotalPlots);
                int totalOwners = items.Sum(x => x.TotalOwners);

                txtTotalUnits.Text = totalUnits.ToString("N0");
                txtTotalKhatians.Text = totalKhatians.ToString("N0");
                txtTotalPlots.Text = totalPlots.ToString("N0");
                txtTotalOwners.Text = totalOwners.ToString("N0");

                txtLoading.Visibility = Visibility.Collapsed;
                dgMigratedUnits.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                txtLoading.Text = $"Notice: Could not load unit breakdown ({ex.Message}).";
                txtLoading.Visibility = Visibility.Visible;
            }
        }

        private List<MigratedUnitSummaryItem> FetchMigratedUnits()
        {
            var result = new List<MigratedUnitSummaryItem>();

            // 1. Fetch unit codes, khatians, plots, owners from source DB
            using (var conn = new SqlConnection(_sourceConnectionString))
            {
                conn.Open();

                string sql = @"
                    IF OBJECT_ID('dbo.tblLinfo01') IS NOT NULL AND OBJECT_ID('dbo.tblLinfo02') IS NOT NULL
                    BEGIN
                        SELECT 
                            COALESCE(p.UnitCode, o.UnitCode) AS UnitCode,
                            ISNULL(p.TotalKhatians, 0) AS TotalKhatians,
                            ISNULL(p.TotalPlots, 0) AS TotalPlots,
                            ISNULL(o.TotalOwners, 0) AS TotalOwners
                        FROM
                        (
                            SELECT 
                                LTRIM(RTRIM(UnitCod)) AS UnitCode,
                                COUNT(DISTINCT KhtNum) AS TotalKhatians,
                                COUNT(*) AS TotalPlots
                            FROM dbo.tblLinfo01
                            WHERE UnitCod IS NOT NULL AND LEN(LTRIM(RTRIM(UnitCod))) > 0
                            GROUP BY LTRIM(RTRIM(UnitCod))
                        ) p
                        FULL OUTER JOIN
                        (
                            SELECT 
                                LTRIM(RTRIM(unitcod)) AS UnitCode,
                                COUNT(*) AS TotalOwners
                            FROM dbo.tblLinfo02
                            WHERE unitcod IS NOT NULL AND LEN(LTRIM(RTRIM(unitcod))) > 0
                            GROUP BY LTRIM(RTRIM(unitcod))
                        ) o ON p.UnitCode = o.UnitCode
                        ORDER BY UnitCode;
                    END
                    ELSE IF OBJECT_ID('dbo.tblLinfo01') IS NOT NULL
                    BEGIN
                        SELECT 
                            LTRIM(RTRIM(UnitCod)) AS UnitCode,
                            COUNT(DISTINCT KhtNum) AS TotalKhatians,
                            COUNT(*) AS TotalPlots,
                            0 AS TotalOwners
                        FROM dbo.tblLinfo01
                        WHERE UnitCod IS NOT NULL AND LEN(LTRIM(RTRIM(UnitCod))) > 0
                        GROUP BY LTRIM(RTRIM(UnitCod))
                        ORDER BY LTRIM(RTRIM(UnitCod));
                    END";

                using var cmd = new SqlCommand(sql, conn);
                cmd.CommandTimeout = 90;
                using var reader = cmd.ExecuteReader();
                int sl = 1;
                while (reader.Read())
                {
                    result.Add(new MigratedUnitSummaryItem
                    {
                        SlNo = sl++,
                        UnitCode = reader.GetString(0),
                        MouzaName = "-",
                        TotalKhatians = reader.GetInt32(1),
                        TotalPlots = reader.GetInt32(2),
                        TotalOwners = reader.GetInt32(3),
                        Status = "✔ Migrated"
                    });
                }
            }

            // 2. Optional: Try to resolve Mouza names from AREAINF in target DB or source DB
            if (!string.IsNullOrEmpty(_targetConnectionString))
            {
                try
                {
                    using var targetConn = new SqlConnection(_targetConnectionString);
                    targetConn.Open();

                    using var nameCmd = new SqlCommand(@"
                        IF OBJECT_ID('dbo.AREAINF') IS NOT NULL
                        BEGIN
                            SELECT LTRIM(RTRIM(unitcod)) AS unitcod, LTRIM(RTRIM(unitname)) AS unitname 
                            FROM dbo.AREAINF 
                            WHERE unitcod IS NOT NULL AND unitname IS NOT NULL;
                        END", targetConn);

                    using var nameReader = nameCmd.ExecuteReader();
                    var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (nameReader.Read())
                    {
                        string code = nameReader.GetString(0);
                        string name = nameReader.GetString(1);
                        if (!nameMap.ContainsKey(code))
                            nameMap[code] = name;
                    }

                    foreach (var item in result)
                    {
                        if (nameMap.TryGetValue(item.UnitCode, out var mouza))
                        {
                            item.MouzaName = mouza;
                        }
                    }
                }
                catch { }
            }

            return result;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
