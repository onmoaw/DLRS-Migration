using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Interaction logic for ViewRecordsWindow.xaml
    /// Model A: Master-Detail Tabbed View for migrated land records in DLRSLSINF_4103PRSDB.
    /// Features:
    ///   - Strictly 7 cascading filters: Division, District, Survey, Upazila/Thana, Mouza, Current Khatian, Owner.
    ///   - Strict 9-digit hierarchical cascading:
    ///       * বিভাগ (XX0000000)
    ///       * জেলা (XXXX00000, excluding XX0000000)
    ///       * উপজেলা/থানা (XXXXXX000, excluding XXXX00000 and XX0000000)
    ///       * মৌজা (XXXXXXXXX, excluding all parent levels)
    ///   - Clean dropdown display: Names only (codes removed from dropdown display values).
    ///   - Direct execution of SP [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS] (zero auto-deployment).
    ///   - Synchronized Master (Khatians) and Detail (Owners, Plots with 4-Ka/Kha/Ga, Audit metadata).
    ///   - Serial number (#) column on the far left of all DataGrids.
    ///   - UTF-8 BOM CSV exports for Khatians and full joined Details.
    /// </summary>
    public partial class ViewRecordsWindow : Window
    {
        private readonly string? _coreConnectionString;
        private List<AreaHierarchyItem> _allAreaItems = new();
        private DataSet? _dsRecords;
        private bool _isPopulatingDropdowns = false;

        public ViewRecordsWindow() : this(null)
        {
        }

        public ViewRecordsWindow(string? coreConnectionString)
        {
            InitializeComponent();
            _coreConnectionString = coreConnectionString;

            Loaded += ViewRecordsWindow_Loaded;
            Unloaded += ViewRecordsWindow_Unloaded;
        }

        private async void ViewRecordsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ThemeChanged += OnThemeChanged;
            UpdateThemeButton();

            if (!string.IsNullOrWhiteSpace(_coreConnectionString))
            {
                txtDbInfo.Text = "Database: DLRSLSINF_4103PRSDB (সংযুক্ত)";
                await InitializeAreaHierarchyAsync();
            }
            else
            {
                txtDbInfo.Text = "Database: সংযোগ পাওয়া যায়নি (অফলাইন মোড)";
                txtStatus.Text = "সতর্কতা: কোনো সক্রিয় ডাটাবেজ সংযোগ প্রদান করা হয়নি। ডেমো এলাকা লোড করা হচ্ছে...";
                LoadFallbackAreaHierarchy();
            }
        }

        private void ViewRecordsWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
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

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #region ================= AREA HIERARCHY CASCADING =================

        /// <summary>
        /// Loads all administrative area records from dbo.AREAINF and populates the cascading dropdowns.
        /// </summary>
        private async Task InitializeAreaHierarchyAsync()
        {
            try
            {
                txtStatus.Text = "ভৌগোলিক এলাকা তথ্য (AREAINF) লোড করা হচ্ছে...";
                btnSearch.IsEnabled = false;

                _allAreaItems = await LandRecordsViewerService.LoadAreaHierarchyAsync(_coreConnectionString!);

                if (_allAreaItems.Count == 0)
                {
                    txtStatus.Text = "AREAINF টেবিলে কোনো এলাকা পাওয়া যায়নি। ডেমো এলাকা ব্যবহার করা হচ্ছে।";
                    LoadFallbackAreaHierarchy();
                    return;
                }

                PopulateDivisions();
                txtStatus.Text = "প্রস্তুত: অনুগ্রহ করে বিভাগ, জেলা, উপজেলা এবং মৌজা নির্বাচন করে খুঁজুন বাটনে ক্লিক করুন।";
            }
            catch (Exception ex)
            {
                txtStatus.Text = "AREAINF লোড করার সময় ত্রুটি ঘটেছে: " + ex.Message;
                LoadFallbackAreaHierarchy();
            }
            finally
            {
                btnSearch.IsEnabled = true;
            }
        }

        private void LoadFallbackAreaHierarchy()
        {
            _allAreaItems = new List<AreaHierarchyItem>
            {
                new AreaHierarchyItem { UnitCod = "010000000", UnitNam = "ঢাকা", UnitType = "বিভাগ" },
                new AreaHierarchyItem { UnitCod = "010500000", UnitNam = "ঢাকা", UnitType = "জেলা" },
                new AreaHierarchyItem { UnitCod = "010502000", UnitNam = "গুলশান / বাড্ডা", UnitType = "উপজেলা/থানা" },
                new AreaHierarchyItem { UnitCod = "010502003", UnitNam = "জোয়ার সাহারা", UnitType = "মৌজা", ResaNum = "81001" },
                new AreaHierarchyItem { UnitCod = "010502005", UnitNam = "ভাটারা", UnitType = "মৌজা", ResaNum = "81002" }
            };

            PopulateDivisions();
        }

        /// <summary>
        /// 1. বিভাগ (Division): Strictly XX0000000 (XX != "00")
        /// </summary>
        private void PopulateDivisions()
        {
            _isPopulatingDropdowns = true;
            try
            {
                cmbDivision.ItemsSource = null;
                cmbDistrict.ItemsSource = null;
                cmbUpazila.ItemsSource = null;
                cmbMouza.ItemsSource = null;

                // বিভাগ: XX0000000
                var divisions = _allAreaItems.Where(a => a.IsDivision).OrderBy(a => a.UnitCod).ToList();

                if (divisions.Count == 0)
                {
                    // Synthesize divisions from distinct 2-digit prefixes if explicit XX0000000 row is missing
                    divisions = _allAreaItems
                        .Where(a => a.UnitCod.Length == 9 && a.DivisionCode != "00")
                        .GroupBy(a => a.DivisionCode)
                        .Select(g => new AreaHierarchyItem
                        {
                            UnitCod = g.Key + "0000000",
                            UnitNam = g.First(x => !string.IsNullOrWhiteSpace(x.UnitNam)).UnitNam
                        })
                        .OrderBy(a => a.UnitCod)
                        .ToList();
                }

                cmbDivision.ItemsSource = divisions;
                cmbDivision.DisplayMemberPath = nameof(AreaHierarchyItem.DisplayName);
                cmbDivision.SelectedValuePath = nameof(AreaHierarchyItem.DivisionCode);

                if (divisions.Count > 0)
                {
                    cmbDivision.SelectedIndex = 0;
                }
            }
            finally
            {
                _isPopulatingDropdowns = false;
            }

            CascadeDistricts();
        }

        private void CmbDivision_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDropdowns) return;
            CascadeDistricts();
        }

        /// <summary>
        /// 2. জেলা (District): Strictly XXXX00000 where Substring(2,2) != "00" (NEVER XX0000000)
        /// </summary>
        private void CascadeDistricts()
        {
            if (cmbDivision.SelectedItem is not AreaHierarchyItem selectedDiv) return;

            _isPopulatingDropdowns = true;
            try
            {
                string divCode = selectedDiv.DivisionCode;

                // জেলা: XXXX00000 strictly excluding XX0000000
                var districts = _allAreaItems
                    .Where(a => a.IsDistrict && a.DivisionCode == divCode)
                    .OrderBy(a => a.UnitCod)
                    .ToList();

                if (districts.Count == 0)
                {
                    districts = _allAreaItems
                        .Where(a => a.UnitCod.Length == 9 && a.DivisionCode == divCode && a.DistrictPart != "00")
                        .GroupBy(a => a.DistrictCode)
                        .Select(g => new AreaHierarchyItem
                        {
                            UnitCod = g.Key + "00000",
                            UnitNam = g.First(x => !string.IsNullOrWhiteSpace(x.UnitNam)).UnitNam
                        })
                        .OrderBy(a => a.UnitCod)
                        .ToList();
                }

                cmbDistrict.ItemsSource = districts;
                cmbDistrict.DisplayMemberPath = nameof(AreaHierarchyItem.DisplayName);
                cmbDistrict.SelectedValuePath = nameof(AreaHierarchyItem.DistrictCode);

                if (districts.Count > 0)
                {
                    cmbDistrict.SelectedIndex = 0;
                }
                else
                {
                    cmbDistrict.ItemsSource = null;
                }
            }
            finally
            {
                _isPopulatingDropdowns = false;
            }

            CascadeUpazilas();
        }

        private void CmbDistrict_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDropdowns) return;
            CascadeUpazilas();
        }

        /// <summary>
        /// 3. উপজেলা/থানা (Upazila): Strictly XXXXXX000 where Substring(4,2) != "00" (NEVER XXXX00000 or XX0000000)
        /// </summary>
        private void CascadeUpazilas()
        {
            if (cmbDistrict.SelectedItem is not AreaHierarchyItem selectedDist) return;

            _isPopulatingDropdowns = true;
            try
            {
                string distCode = selectedDist.DistrictCode;

                // উপজেলা/থানা: XXXXXX000 strictly excluding district and division
                var upazilas = _allAreaItems
                    .Where(a => a.IsUpazila && a.DistrictCode == distCode)
                    .OrderBy(a => a.UnitCod)
                    .ToList();

                if (upazilas.Count == 0)
                {
                    upazilas = _allAreaItems
                        .Where(a => a.UnitCod.Length == 9 && a.DistrictCode == distCode && a.UpazilaPart != "00")
                        .GroupBy(a => a.UpazilaCode)
                        .Select(g => new AreaHierarchyItem
                        {
                            UnitCod = g.Key + "000",
                            UnitNam = g.First(x => !string.IsNullOrWhiteSpace(x.UnitNam)).UnitNam
                        })
                        .OrderBy(a => a.UnitCod)
                        .ToList();
                }

                cmbUpazila.ItemsSource = upazilas;
                cmbUpazila.DisplayMemberPath = nameof(AreaHierarchyItem.DisplayName);
                cmbUpazila.SelectedValuePath = nameof(AreaHierarchyItem.UpazilaCode);

                if (upazilas.Count > 0)
                {
                    cmbUpazila.SelectedIndex = 0;
                }
                else
                {
                    cmbUpazila.ItemsSource = null;
                }
            }
            finally
            {
                _isPopulatingDropdowns = false;
            }

            CascadeMouzas();
        }

        private void CmbUpazila_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDropdowns) return;
            CascadeMouzas();
        }

        /// <summary>
        /// 4. মৌজা (Mouza): Strictly XXXXXXXXX where Substring(6,3) != "000" (NEVER parent levels)
        /// </summary>
        private void CascadeMouzas()
        {
            if (cmbUpazila.SelectedItem is not AreaHierarchyItem selectedUpazila) return;

            _isPopulatingDropdowns = true;
            try
            {
                string upazilaCode = selectedUpazila.UpazilaCode;

                // মৌজা: XXXXXXXXX strictly leaf nodes
                var mouzas = _allAreaItems
                    .Where(a => a.IsMouza && a.UpazilaCode == upazilaCode)
                    .OrderBy(a => a.UnitCod)
                    .ToList();

                cmbMouza.ItemsSource = mouzas;
                cmbMouza.DisplayMemberPath = nameof(AreaHierarchyItem.DisplayName);
                cmbMouza.SelectedValuePath = nameof(AreaHierarchyItem.UnitCod);

                if (mouzas.Count > 0)
                {
                    cmbMouza.SelectedIndex = 0;
                }
                else
                {
                    cmbMouza.ItemsSource = null;
                }
            }
            finally
            {
                _isPopulatingDropdowns = false;
            }

            UpdateActiveMouzaBadge();
        }

        private void CmbMouza_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDropdowns) return;
            UpdateActiveMouzaBadge();
        }

        private void UpdateActiveMouzaBadge()
        {
            if (cmbMouza.SelectedItem is AreaHierarchyItem mouza)
            {
                bdActiveMouzaBadge.Visibility = Visibility.Visible;
                txtActiveMouzaBadge.Text = $"মৌজা: {mouza.UnitNam}";
                txtStatus.Text = $"নির্বাচিত মৌজা: {mouza.UnitNam} — 'খুঁজুন' বাটনে ক্লিক করে রেকর্ড লোড করুন।";
            }
            else
            {
                bdActiveMouzaBadge.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region ================= SEARCH & DATA RETRIEVAL =================

        /// <summary>
        /// Executes the dedicated stored procedure [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS]
        /// against the core database for the selected Mouza, Survey, Khatian and Owner.
        /// </summary>
        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            if (cmbMouza.SelectedItem is not AreaHierarchyItem selectedMouza)
            {
                MessageBox.Show(
                    "অনুগ্রহ করে ড্রপডাউন থেকে একটি মৌজা নির্বাচন করুন।",
                    "মৌজা নির্বাচন আবশ্যক",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_coreConnectionString))
            {
                MessageBox.Show(
                    "ডাটাবেজ সংযোগ সক্রিয় নয়। অনুগ্রহ করে মূল উইন্ডো থেকে ডাটাবেজ সংযোগ নিশ্চিত করুন।",
                    "ডাটাবেজ সংযোগ অনুপস্থিত",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string unitCod = selectedMouza.UnitCod;
            string rsNum = "201901";
            if (cmbSurvey.SelectedItem is ComboBoxItem cbi && cbi.Tag != null)
            {
                rsNum = cbi.Tag.ToString() ?? "201901";
            }

            string currentKhatian = txtCurrentKhatian.Text.Trim();
            string ownerName = txtOwner.Text.Trim();

            btnSearch.IsEnabled = false;
            btnReset.IsEnabled = false;
            txtStatus.Text = $"[{selectedMouza.UnitNam}] এর রেকর্ডসমূহ ডাটাবেজ থেকে সংগ্রহ করা হচ্ছে...";

            try
            {
                _dsRecords = await LandRecordsViewerService.GetMigratedLandRecordsAsync(
                    _coreConnectionString,
                    "4103",
                    rsNum,
                    unitCod,
                    string.IsNullOrWhiteSpace(currentKhatian) ? null : currentKhatian,
                    string.IsNullOrWhiteSpace(ownerName) ? null : ownerName);

                DataTable dtKhatians = _dsRecords.Tables["Khatians"];
                dgKhatians.ItemsSource = dtKhatians.DefaultView;
                txtKhatianCount.Text = $"মোট খতিয়ান: {dtKhatians.Rows.Count}টি";

                if (dtKhatians.Rows.Count > 0)
                {
                    dgKhatians.SelectedIndex = 0;
                    txtStatus.Text = $"অনুসন্ধান সম্পন্ন: {dtKhatians.Rows.Count}টি খতিয়ান লোড হয়েছে।";
                }
                else
                {
                    dgOwners.ItemsSource = null;
                    dgPlots.ItemsSource = null;
                    txtOwnerCount.Text = "মালিক সংখ্যা: ০ জন";
                    txtPlotCount.Text = "দাগ সংখ্যা: ০টি";
                    ClearAuditFields();
                    txtSelectedKhatianHeader.Text = "কোনো খতিয়ান পাওয়া যায়নি";
                    txtStatus.Text = "প্রদত্ত ফিল্টারের সাথে কোনো খতিয়ান মেলেনি। ফিল্টার পরিবর্তন করে পুনরায় চেষ্টা করুন।";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "ডাটা সংগ্রহের সময় ত্রুটি: " + ex.Message;
                MessageBox.Show(
                    "রেকর্ড অনুসন্ধানের সময় ডাটাবেজে ত্রুটি হয়েছে:\n\n" + ex.Message +
                    "\n\nঅনুগ্রহ করে নিশ্চিত করুন [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS] ডাটাবেজে তৈরি করা আছে।",
                    "Database Query Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                btnSearch.IsEnabled = true;
                btnReset.IsEnabled = true;
            }
        }

        /// <summary>
        /// Synchronizes the detail tabs (Owners, Plots, and Audit) when a Khatian row is selected.
        /// </summary>
        private void DgKhatians_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgKhatians.SelectedItem is not DataRowView drv)
            {
                return;
            }

            string currentKhatian = drv["CurrentKhatian"]?.ToString() ?? string.Empty;
            string formerKhatian = drv["FormerKhatian"]?.ToString() ?? string.Empty;
            string mouzaName = drv["MouzaName"]?.ToString() ?? string.Empty;
            string surveyName = drv["SurveyName"]?.ToString() ?? string.Empty;

            txtSelectedKhatianHeader.Text =
                $"🔍 নির্বাচিত খতিয়ান নং: {currentKhatian} (সাবেক: {(string.IsNullOrWhiteSpace(formerKhatian) ? "—" : formerKhatian)}) | মৌজা: {mouzaName} | জরিপ: {surveyName}";

            // 1. Synchronize Owners Tab
            if (_dsRecords != null && _dsRecords.Tables.Contains("Owners"))
            {
                DataTable dtOwners = _dsRecords.Tables["Owners"];
                var dvOwners = new DataView(dtOwners)
                {
                    RowFilter = $"CurrentKhatian = '{currentKhatian.Replace("'", "''")}'"
                };
                dgOwners.ItemsSource = dvOwners;
                txtOwnerCount.Text = $"মালিক সংখ্যা: {dvOwners.Count} জন";
            }

            // 2. Synchronize Plots Tab (with dynamic 4-Ka, 4-Kha, 4-Ga)
            if (_dsRecords != null && _dsRecords.Tables.Contains("Plots"))
            {
                DataTable dtPlots = _dsRecords.Tables["Plots"];
                var dvPlots = new DataView(dtPlots)
                {
                    RowFilter = $"CurrentKhatian = '{currentKhatian.Replace("'", "''")}'"
                };
                dgPlots.ItemsSource = dvPlots;
                txtPlotCount.Text = $"দাগ সংখ্যা: {dvPlots.Count}টি";
            }

            // 3. Synchronize Audit & Metadata Tab
            txtAuditEntryKhatian.Text = drv["EntryKhatian"]?.ToString() ?? "—";
            txtAuditSheet.Text = drv["SheetNum"]?.ToString() ?? "—";
            txtAuditNote.Text = drv["KhatianNote"]?.ToString() ?? "—";
            txtAuditRemarks.Text = drv["KhatianRemarks"]?.ToString() ?? "—";
            txtAuditUserId.Text = drv["EntryUserId"]?.ToString() ?? "—";

            string sealId = drv["SealId"]?.ToString() ?? "";
            string sealDesc = drv["SealDesc"]?.ToString() ?? "";
            txtAuditSeal.Text = !string.IsNullOrWhiteSpace(sealDesc)
                ? $"{sealId} - {sealDesc}"
                : (!string.IsNullOrWhiteSpace(sealId) ? sealId : "—");

            txtAuditTerminal.Text = drv["TerminalId"]?.ToString() ?? "—";
            txtAuditTime.Text = drv["EntryTime"]?.ToString() ?? "—";
        }

        private void ClearAuditFields()
        {
            txtAuditEntryKhatian.Text = "—";
            txtAuditSheet.Text = "—";
            txtAuditNote.Text = "—";
            txtAuditRemarks.Text = "—";
            txtAuditUserId.Text = "—";
            txtAuditSeal.Text = "—";
            txtAuditTerminal.Text = "—";
            txtAuditTime.Text = "—";
        }

        /// <summary>
        /// Clears all input search filters and resets selection.
        /// </summary>
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            txtCurrentKhatian.Text = string.Empty;
            txtOwner.Text = string.Empty;
            cmbSurvey.SelectedIndex = 0;

            if (cmbDivision.Items.Count > 0)
            {
                cmbDivision.SelectedIndex = 0;
            }

            dgKhatians.ItemsSource = null;
            dgOwners.ItemsSource = null;
            dgPlots.ItemsSource = null;

            txtKhatianCount.Text = "মোট খতিয়ান: ০টি";
            txtOwnerCount.Text = "মালিক সংখ্যা: ০ জন";
            txtPlotCount.Text = "দাগ সংখ্যা: ০টি";
            ClearAuditFields();

            txtSelectedKhatianHeader.Text = "🔍 নির্বাচিত খতিয়ানের বিস্তারিত বিবরণ: (উপরে তালিকা থেকে একটি খতিয়ান নির্বাচন করুন)";
            txtStatus.Text = "ফিল্টার রিসেট করা হয়েছে। অনুগ্রহ করে নতুন করে অনুসন্ধান করুন।";
        }

        #endregion

        #region ================= CSV EXPORTS (UTF-8 BOM) =================

        /// <summary>
        /// Exports the Khatians master list to CSV with UTF-8 BOM encoding.
        /// </summary>
        private void BtnExportKhatiansCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_dsRecords == null || !_dsRecords.Tables.Contains("Khatians") || _dsRecords.Tables["Khatians"].Rows.Count == 0)
            {
                MessageBox.Show("এক্সপোর্ট করার মতো কোনো খতিয়ান ডাটা নেই।", "তথ্য নেই", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "খতিয়ান তালিকা এক্সপোর্ট করুন (CSV)",
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"Khatians_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                DataTable dt = _dsRecords.Tables["Khatians"];

                // Header
                var colHeaders = new List<string>();
                foreach (DataColumn col in dt.Columns)
                {
                    colHeaders.Add($"\"{col.ColumnName}\"");
                }
                sb.AppendLine(string.Join(",", colHeaders));

                // Rows
                foreach (DataRow row in dt.Rows)
                {
                    var fields = new List<string>();
                    foreach (DataColumn col in dt.Columns)
                    {
                        string val = row[col]?.ToString() ?? "";
                        fields.Add($"\"{val.Replace("\"", "\"\"")}\"");
                    }
                    sb.AppendLine(string.Join(",", fields));
                }

                // Write with UTF-8 BOM for proper Bengali rendering in Excel
                File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));

                MessageBox.Show(
                    $"খতিয়ান তালিকা সফলভাবে এক্সপোর্ট করা হয়েছে:\n\n{sfd.FileName}",
                    "এক্সপোর্ট সম্পন্ন",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("CSV এক্সপোর্টে ত্রুটি: " + ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Exports complete detailed records (Khatian + Owners + Plots) to CSV.
        /// </summary>
        private void BtnExportDetailsCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_dsRecords == null || !_dsRecords.Tables.Contains("Khatians") || _dsRecords.Tables["Khatians"].Rows.Count == 0)
            {
                MessageBox.Show("এক্সপোর্ট করার মতো কোনো বিস্তারিত ডাটা নেই।", "তথ্য নেই", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "সম্পূর্ণ বিস্তারিত রেকর্ড এক্সপোর্ট করুন (CSV)",
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"LandRecords_FullDetail_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();

                // Detailed Combined Headers
                sb.AppendLine("SlNo,CurrentKhatian,FormerKhatian,MouzaName,SurveyName,SheetNum,LandTypeName,PorchaTitle,OwnerSerial,OwnerName,FatherName,MotherName,SpouseName,Nid,BirthReg,Mobile,OwnerAddress,OwnerShareArea,PlotSerial,CurrentPlot,FormerPlot,CsPlot,AgriLandClass_4Ka,NonAgriLandClass_4Kha,LocalName_4Ga,TotalPlotArea,KhatianShare,KhatianPlotArea,PlotRemarks");

                DataTable dtKht = _dsRecords.Tables["Khatians"];
                DataTable? dtOwners = _dsRecords.Tables.Contains("Owners") ? _dsRecords.Tables["Owners"] : null;
                DataTable? dtPlots = _dsRecords.Tables.Contains("Plots") ? _dsRecords.Tables["Plots"] : null;

                int rowCount = 0;
                foreach (DataRow khtRow in dtKht.Rows)
                {
                    string khtNum = khtRow["CurrentKhatian"]?.ToString() ?? "";
                    var ownerRows = dtOwners?.Select($"CurrentKhatian = '{khtNum.Replace("'", "''")}'") ?? Array.Empty<DataRow>();
                    var plotRows = dtPlots?.Select($"CurrentKhatian = '{khtNum.Replace("'", "''")}'") ?? Array.Empty<DataRow>();

                    int maxRows = Math.Max(ownerRows.Length, plotRows.Length);
                    if (maxRows == 0) maxRows = 1;

                    for (int i = 0; i < maxRows; i++)
                    {
                        rowCount++;
                        var oRow = i < ownerRows.Length ? ownerRows[i] : null;
                        var pRow = i < plotRows.Length ? plotRows[i] : null;

                        var fields = new List<string>
                        {
                            rowCount.ToString(),
                            CsvEscape(khtRow["CurrentKhatian"]),
                            CsvEscape(khtRow["FormerKhatian"]),
                            CsvEscape(khtRow["MouzaName"]),
                            CsvEscape(khtRow["SurveyName"]),
                            CsvEscape(khtRow["SheetNum"]),
                            CsvEscape(khtRow["LandTypeName"]),
                            CsvEscape(khtRow["PorchaTitle"]),
                            CsvEscape(oRow?["OwnerSerial"]),
                            CsvEscape(oRow?["OwnerName"]),
                            CsvEscape(oRow?["FatherName"]),
                            CsvEscape(oRow?["MotherName"]),
                            CsvEscape(oRow?["SpouseName"]),
                            CsvEscape(oRow?["Nid"]),
                            CsvEscape(oRow?["BirthReg"]),
                            CsvEscape(oRow?["Phone"]),
                            CsvEscape(oRow?["Address"]),
                            CsvEscape(oRow?["OwnerShareArea"]),
                            CsvEscape(pRow?["PlotSerial"]),
                            CsvEscape(pRow?["CurrentPlot"]),
                            CsvEscape(pRow?["FormerPlot"]),
                            CsvEscape(pRow?["CsPlot"]),
                            CsvEscape(pRow?["AgriLandClass_4Ka"]),
                            CsvEscape(pRow?["NonAgriLandClass_4Kha"]),
                            CsvEscape(pRow?["LocalName_4Ga"]),
                            CsvEscape(pRow?["TotalPlotArea"]),
                            CsvEscape(pRow?["KhatianShare"]),
                            CsvEscape(pRow?["KhatianPlotArea"]),
                            CsvEscape(pRow?["PlotRemarks"])
                        };

                        sb.AppendLine(string.Join(",", fields));
                    }
                }

                File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));

                MessageBox.Show(
                    $"সম্পূর্ণ বিস্তারিত রেকর্ড সফলভাবে এক্সপোর্ট করা হয়েছে ({rowCount} সারি):\n\n{sfd.FileName}",
                    "এক্সপোর্ট সম্পন্ন",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("CSV এক্সপোর্টে ত্রুটি: " + ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string CsvEscape(object? obj)
        {
            if (obj == null || obj == DBNull.Value) return "\"\"";
            string s = obj.ToString() ?? "";
            return $"\"{s.Replace("\"", "\"\"")}\"";
        }

        #endregion
    }
}
