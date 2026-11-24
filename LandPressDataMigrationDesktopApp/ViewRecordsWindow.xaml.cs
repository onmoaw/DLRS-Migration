using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Interaction logic for ViewRecordsWindow.xaml
    /// Side-by-Side Master-Detail View for migrated land records in DLRSLSINF_4103PRSDB:
    ///   - Left Pane: Master Khatian list (# Serial, Current Khatian, Owners, Status).
    ///   - Right Pane: All-in-one Single-Page Preview without tabs (Overview card, Owners table, Plots table, Audit card).
    ///   - Strict 7 cascading filters with 9-digit hierarchical breakdown.
    ///   - Clean display: purely administrative Bengali names without raw numeric codes.
    ///   - Full loading overlay during search and data loading.
    ///   - Proportionally scaled typography throughout.
    ///   - Direct execution of SP [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS] (zero auto-deployment).
    ///   - Serial number (#) column on the far left of all DataGrids.
    ///   - UTF-8 BOM CSV exports for Master list and complete detailed records.
    /// </summary>
    public partial class ViewRecordsWindow : Window
    {
        private readonly string? _coreConnectionString;
        private List<AreaHierarchyItem> _allAreaItems = new();
        private DataSet? _dsRecords;
        private bool _isPopulatingDropdowns = false;
        private List<KhatianReportRow> _currentKhatianReportRows = new();

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

            // Apply strict role-based access permissions (Normal users: view only, no exports/print)
            ApplyUserRolePermissions();

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

        #region ================= LOADER OVERLAY =================

        private void ShowLoader(string message = "অনুগ্রহ করে অপেক্ষা করুন...", string? subMessage = null)
        {
            txtLoadingMessage.Text = message;
            if (!string.IsNullOrWhiteSpace(subMessage))
            {
                txtLoadingSubMessage.Text = subMessage;
            }
            loadingOverlay.Visibility = Visibility.Visible;

            // UI-adjusted search controls & button feedback
            btnSearch.Content = "⏳ অনুসন্ধান হচ্ছে...";
            btnSearch.IsEnabled = false;
            btnReset.IsEnabled = false;
            searchProgressBar.Visibility = Visibility.Visible;

            // Master List inline loading state
            txtListLoadingTitle.Text = message;
            txtListLoadingSub.Text = subMessage ?? "ডাটাবেজ থেকে তথ্য আনা হচ্ছে, অনুগ্রহ করে অপেক্ষা করুন...";
            pnlListLoading.Visibility = Visibility.Visible;
        }

        private void HideLoader()
        {
            loadingOverlay.Visibility = Visibility.Collapsed;
            pnlListLoading.Visibility = Visibility.Collapsed;
            searchProgressBar.Visibility = Visibility.Collapsed;
            btnSearch.Content = "🔍 খুঁজুন / Search";
            btnSearch.IsEnabled = true;
            btnReset.IsEnabled = true;
        }

        #endregion

        #region ================= AREA HIERARCHY CASCADING =================

        /// <summary>
        /// Loads all administrative area records from dbo.AREAINF and populates cascading dropdowns.
        /// </summary>
        private async Task InitializeAreaHierarchyAsync()
        {
            ShowLoader("ভৌগোলিক এলাকা তথ্য (AREAINF) লোড করা হচ্ছে...", "বিভাগ, জেলা, উপজেলা এবং মৌজার তালিকা প্রস্তুত করা হচ্ছে...");
            await Task.Delay(50);
            try
            {
                txtStatus.Text = "ভৌগোলিক এলাকা তথ্য (AREAINF) লোড করা হচ্ছে...";

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
                HideLoader();
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

            // Normalize input so that search works with both Bangla and English digits
            string currentKhatianInput = txtCurrentKhatian.Text.Trim();
            string currentKhatian = BanglaNumberConverter.ToEnglishDigit(currentKhatianInput);
            string ownerName = txtOwner.Text.Trim();

            txtStatus.Text = $"[{selectedMouza.UnitNam}] এর রেকর্ডসমূহ ডাটাবেজ থেকে সংগ্রহ করা হচ্ছে...";
            ShowLoader(
                $"[{selectedMouza.UnitNam}] এর রেকর্ড অনুসন্ধান হচ্ছে...",
                "ডাটাবেজ থেকে খতিয়ান, মালিক ও দাগ ভিত্তিক ভূমি রেকর্ড সংগ্রহ করা হচ্ছে...");

            // Yield to UI dispatcher to immediately draw the loader and start animation before query execution
            await Task.Delay(50);

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
                txtKhatianCount.Text = $"{BanglaNumberConverter.ToBanglaDigit(dtKhatians.Rows.Count)}টি";

                if (dtKhatians.Rows.Count > 0)
                {
                    dgKhatians.SelectedIndex = 0;
                    txtStatus.Text = $"অনুসন্ধান সম্পন্ন: {BanglaNumberConverter.ToBanglaDigit(dtKhatians.Rows.Count)}টি খতিয়ান লোড হয়েছে।";
                }
                else
                {
                    ClearPreview();
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
                HideLoader();
            }
        }

        /// <summary>
        /// Synchronizes the single-page preview panel when a Khatian row is selected in the master list.
        /// </summary>
        private void DgKhatians_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgKhatians.SelectedItem is not DataRowView drv)
            {
                ClearPreview();
                return;
            }

            // Show preview content, hide empty state
            pnlEmptyPreview.Visibility = Visibility.Collapsed;
            pnlPreviewContent.Visibility = Visibility.Visible;

            string currentKhatian = drv["CurrentKhatian"]?.ToString() ?? string.Empty;
            string bnCurrentKhatian = BanglaNumberConverter.ToBanglaDigit(currentKhatian);
            string bnFormerKhatian = BanglaNumberConverter.ToBanglaDigit(drv["FormerKhatian"]);
            string bnEntryKhatian = BanglaNumberConverter.ToBanglaDigit(drv["EntryKhatian"]);
            string bnSheet = BanglaNumberConverter.ToBanglaDigit(drv["SheetNum"]);
            string mouzaName = drv["MouzaName"]?.ToString() ?? string.Empty;
            string surveyName = drv["SurveyName"]?.ToString() ?? string.Empty;

            // Highlighted Banner and Hero Badge
            txtPrevHeroKhatianBadge.Text = $"খতিয়ান: {bnCurrentKhatian}";
            txtSelectedKhatianHeader.Text =
                $"🔍 খতিয়ান বিবরণী: খতিয়ান নং {bnCurrentKhatian} | মৌজা: {mouzaName} | জরিপ: {surveyName}";

            // Section 1: Identification & Overview Card
            txtPrevCurrentKhatian.Text = bnCurrentKhatian;
            txtPrevFormerKhatian.Text = !string.IsNullOrWhiteSpace(bnFormerKhatian) ? bnFormerKhatian : "—";
            txtPrevEntryKhatian.Text = !string.IsNullOrWhiteSpace(bnEntryKhatian) ? bnEntryKhatian : "—";
            txtPrevSheet.Text = !string.IsNullOrWhiteSpace(bnSheet) ? bnSheet : "—";
            txtPrevMouza.Text = mouzaName;
            txtPrevSurvey.Text = surveyName;
            txtPrevLandType.Text = drv["LandTypeName"]?.ToString() ?? "—";
            txtPrevPorchaTitle.Text = drv["PorchaTitle"]?.ToString() ?? "—";
            txtPrevTotalPlots.Text = $"{BanglaNumberConverter.ToBanglaDigit(drv["TotalPlots"])} টি";
            txtPrevTotalArea.Text = $"{BanglaNumberConverter.FormatBanglaDecimal(Convert.ToDecimal(drv["TotalArea"] ?? 0))} একর";
            txtPrevBadgeStatus.Text = drv["KhatianStatus"]?.ToString() ?? "খসড়া";
            txtPrevKhatianRemarks.Text = drv["KhatianRemarks"]?.ToString() ?? "—";

            // Section 2: Synchronize Owners Table
            DataView? dvOwners = null;
            if (_dsRecords != null && _dsRecords.Tables.Contains("Owners"))
            {
                DataTable dtOwners = _dsRecords.Tables["Owners"];
                dvOwners = new DataView(dtOwners)
                {
                    RowFilter = $"CurrentKhatian = '{currentKhatian.Replace("'", "''")}'"
                };
                dgOwners.ItemsSource = dvOwners;
                txtOwnerCount.Text = $"মালিক সংখ্যা: {BanglaNumberConverter.ToBanglaDigit(dvOwners.Count)} জন";
            }

            // Section 3: Synchronize Plots Table (with dynamic 4-Ka, 4-Kha, 4-Ga)
            DataView? dvPlots = null;
            if (_dsRecords != null && _dsRecords.Tables.Contains("Plots"))
            {
                DataTable dtPlots = _dsRecords.Tables["Plots"];
                dvPlots = new DataView(dtPlots)
                {
                    RowFilter = $"CurrentKhatian = '{currentKhatian.Replace("'", "''")}'"
                };
                dgPlots.ItemsSource = dvPlots;
                txtPlotCount.Text = $"দাগ সংখ্যা: {BanglaNumberConverter.ToBanglaDigit(dvPlots.Count)}টি";
            }

            // Synchronize Khatian View (Official Form 5462 RDLC Document)
            pnlKhatianEmpty.Visibility = Visibility.Collapsed;
            svKhatianDoc.Visibility = Visibility.Visible;
            RenderKhatianReportView(drv, dvOwners, dvPlots);

            // Section 4: System Audit & Metadata Card (ADMIN ONLY)
            if (UserSession.IsAdmin)
            {
                cardAuditSection.Visibility = Visibility.Visible;
                txtAuditUserId.Text = drv["EntryUserId"]?.ToString() ?? "—";

                string sealId = BanglaNumberConverter.ToBanglaDigit(drv["SealId"]);
                string sealDesc = drv["SealDesc"]?.ToString() ?? "";
                txtAuditSeal.Text = !string.IsNullOrWhiteSpace(sealDesc)
                    ? $"{sealId} - {sealDesc}"
                    : (!string.IsNullOrWhiteSpace(sealId) ? sealId : "—");

                txtAuditTerminal.Text = drv["TerminalId"]?.ToString() ?? "—";
                txtAuditTime.Text = BanglaNumberConverter.ToBanglaDigit(drv["EntryTime"]?.ToString() ?? "—");
                txtAuditNote.Text = drv["KhatianNote"]?.ToString() ?? "—";
                txtAuditRemarks.Text = drv["KhatianRemarks"]?.ToString() ?? "—";
            }
            else
            {
                cardAuditSection.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearPreview()
        {
            pnlEmptyPreview.Visibility = Visibility.Visible;
            pnlPreviewContent.Visibility = Visibility.Collapsed;

            dgOwners.ItemsSource = null;
            dgPlots.ItemsSource = null;
            txtOwnerCount.Text = "মালিক সংখ্যা: ০ জন";
            txtPlotCount.Text = "দাগ সংখ্যা: ০টি";

            txtPrevHeroKhatianBadge.Text = "খতিয়ান: —";
            txtSelectedKhatianHeader.Text = "🔍 নির্বাচিত খতিয়ানের পূর্ণাঙ্গ বিবরণী (Preview)";

            txtPrevCurrentKhatian.Text = "—";
            txtPrevFormerKhatian.Text = "—";
            txtPrevEntryKhatian.Text = "—";
            txtPrevSheet.Text = "—";
            txtPrevMouza.Text = "—";
            txtPrevSurvey.Text = "—";
            txtPrevLandType.Text = "—";
            txtPrevPorchaTitle.Text = "—";
            txtPrevTotalPlots.Text = "০ টি";
            txtPrevTotalArea.Text = "০.০০০০ একর";
            txtPrevBadgeStatus.Text = "—";
            txtPrevKhatianRemarks.Text = "—";

            txtAuditNote.Text = "—";
            txtAuditRemarks.Text = "—";
            txtAuditUserId.Text = "—";
            txtAuditSeal.Text = "—";
            txtAuditTerminal.Text = "—";
            txtAuditTime.Text = "—";

            cardAuditSection.Visibility = UserSession.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

            // Clear Khatian View
            pnlKhatianEmpty.Visibility = Visibility.Visible;
            svKhatianDoc.Visibility = Visibility.Collapsed;
            icKhatianRows.ItemsSource = null;
            txtKhatianViewHeroBadge.Text = "খতিয়ান: —";
            rptKhatianNo.Text = "—";
            rptFormerKhatianNo.Text = "—";
            rptTotalOwnerShare.Text = "—";
            rptTotalPlotAcre.Text = "—";
            rptTotalPlotAjutangsho.Text = "—";
            rptTotalKhatianAcre.Text = "—";
            rptTotalKhatianAjutangsho.Text = "—";
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
            txtKhatianCount.Text = "০টি";
            ClearPreview();

            txtStatus.Text = "ফিল্টার রিসেট করা হয়েছে। অনুগ্রহ করে নতুন করে অনুসন্ধান করুন।";
        }

        #endregion

        #region ================= CSV EXPORTS (UTF-8 BOM) =================

        /// <summary>
        /// Exports the Khatians master list to CSV with UTF-8 BOM encoding.
        /// </summary>
        private void BtnExportKhatiansCsv_Click(object sender, RoutedEventArgs e)
        {
            if (!UserSession.IsAdmin)
            {
                MessageBox.Show("শুধুমাত্র অ্যাডমিন ব্যবহারকারীদের জন্য এই সুবিধাটি অনুমোদিত।", "অনুমতি সীমাবদ্ধ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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
            if (!UserSession.IsAdmin)
            {
                MessageBox.Show("শুধুমাত্র অ্যাডমিন ব্যবহারকারীদের জন্য এই সুবিধাটি অনুমোদিত।", "অনুমতি সীমাবদ্ধ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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

        #region ================= ROLE PERMISSIONS & VIEW SWITCHER =================

        /// <summary>
        /// Applies role-based access control:
        ///   - Normal users can ONLY view reports (Software View & Khatian View), search, zoom, and close.
        ///   - Normal users have CSV exports, admin print, and audit trails completely hidden.
        /// </summary>
        private void ApplyUserRolePermissions()
        {
            bool isAdmin = UserSession.IsAdmin;
            btnExportKhatiansCsv.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            btnExportDetailsCsv.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            btnKhatianPrint.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            cardAuditSection.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Handles switching between '💻 সফটওয়্যার ভিউ' and '📜 খতিয়ান ভিউ (পর্চা)'.
        /// </summary>
        private void ViewTab_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlSoftwareView == null || pnlKhatianView == null) return;

            if (rbSoftwareView?.IsChecked == true)
            {
                pnlSoftwareView.Visibility = Visibility.Visible;
                pnlKhatianView.Visibility = Visibility.Collapsed;
            }
            else if (rbKhatianView?.IsChecked == true)
            {
                pnlSoftwareView.Visibility = Visibility.Collapsed;
                pnlKhatianView.Visibility = Visibility.Visible;
                Dispatcher.BeginInvoke(new Action(FitKhatianToWidth), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        #endregion

        #region ================= KHATIAN VIEW (FORM 5462) RENDERING & ZOOM =================

        private void FitKhatianToWidth()
        {
            if (svKhatianDoc == null) return;
            double availableWidth = svKhatianDoc.ActualWidth - 52;
            if (availableWidth > 300)
            {
                double targetZoom = Math.Clamp(Math.Round(availableWidth / 1122.0, 2), 0.5, 2.5);
                SetZoom(targetZoom);
            }
        }

        private void BtnZoomFitWidth_Click(object sender, RoutedEventArgs e)
        {
            FitKhatianToWidth();
        }

        private void SetZoom(double zoom)
        {
            zoom = Math.Clamp(zoom, 0.5, 2.5);
            if (stKhatianZoom != null)
            {
                stKhatianZoom.ScaleX = zoom;
                stKhatianZoom.ScaleY = zoom;
            }
            if (txtKhatianZoomLevel != null)
            {
                txtKhatianZoomLevel.Text = $"{(int)Math.Round(zoom * 100)}%";
                txtKhatianZoomLevel.Text = BanglaNumberConverter.ToBanglaDigit(txtKhatianZoomLevel.Text);
            }
            if (slKhatianZoom != null && Math.Abs(slKhatianZoom.Value - zoom) > 0.01)
            {
                slKhatianZoom.Value = zoom;
            }
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            SetZoom((stKhatianZoom?.ScaleX ?? 1.0) + 0.1);
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            SetZoom((stKhatianZoom?.ScaleX ?? 1.0) - 0.1);
        }

        private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(1.0);
        }

        private void SlKhatianZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SetZoom(e.NewValue);
        }

        private void SvKhatianDoc_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                double currentZoom = stKhatianZoom?.ScaleX ?? 1.0;
                double delta = e.Delta > 0 ? 0.1 : -0.1;
                SetZoom(currentZoom + delta);
            }
        }

        private void BtnKhatianPrint_Click(object sender, RoutedEventArgs e)
        {
            if (!UserSession.IsAdmin)
            {
                MessageBox.Show("শুধুমাত্র অ্যাডমিন ব্যবহারকারীদের জন্য প্রিন্ট সুবিধা প্রযোজ্য।", "অনুমতি সীমাবদ্ধ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var printDlg = new System.Windows.Controls.PrintDialog();
                if (printDlg.ShowDialog() == true)
                {
                    double prevScale = stKhatianZoom?.ScaleX ?? 1.0;
                    if (stKhatianZoom != null)
                    {
                        stKhatianZoom.ScaleX = 1.0;
                        stKhatianZoom.ScaleY = 1.0;
                    }

                    if (printDlg.PrintTicket != null)
                    {
                        printDlg.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;
                    }

                    printDlg.PrintVisual(pnlKhatianDocSheet, $"Khatian_{rptKhatianNo.Text}");

                    if (stKhatianZoom != null)
                    {
                        stKhatianZoom.ScaleX = prevScale;
                        stKhatianZoom.ScaleY = prevScale;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"প্রিন্ট করার সময় সমস্যা হয়েছে:\n{ex.Message}", "প্রিন্ট ত্রুটি", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Generates a high-resolution, vector-accurate HTML view of Form 5462
        /// and opens it in the default system browser (Microsoft Edge/Chrome).
        /// This provides razor-sharp text, sub-pixel typography, and infinite vector zooming (up to 500%).
        /// </summary>
        private void BtnKhatianBrowserView_Click(object sender, RoutedEventArgs e)
        {
            if (dgKhatians.SelectedItem is not DataRowView drv)
            {
                MessageBox.Show("অনুগ্রহ করে প্রথমে বামের তালিকা থেকে একটি খতিয়ান নির্বাচন করুন।", "খতিয়ান নির্বাচন", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string khatianNo = rptKhatianNo.Text;
                string surveyName = rptSurveyName.Text;
                string formerKhatian = rptFormerKhatianNo.Text;
                string division = rptDivision.Text;
                string district = rptDistrict.Text;
                string upazila = rptUpazila.Text;
                string mouza = rptMouza.Text;
                string jlNo = rptJLNo.Text;
                string sheetNo = rptSheetNo.Text;

                string totOwnerShare = rptTotalOwnerShare.Text;
                string totPlotAcre = rptTotalPlotAcre.Text;
                string totPlotAju = rptTotalPlotAjutangsho.Text;
                string totKhtAcre = rptTotalKhatianAcre.Text;
                string totKhtAju = rptTotalKhatianAjutangsho.Text;

                var sbRows = new StringBuilder();
                foreach (var row in _currentKhatianReportRows)
                {
                    sbRows.Append("<tr>");
                    sbRows.Append($"<td class=\"text-left\">{System.Net.WebUtility.HtmlEncode(row.OwnerDetails)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.OwnerShare)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.FormerPlot)}</td>");
                    sbRows.Append($"<td class=\"text-center font-bold\">{System.Net.WebUtility.HtmlEncode(row.CurrentPlot)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.AgriClass_4Ka)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.NonAgriClass_4Kha)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.LocalName_4Ga)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.TotalPlotAcre)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.TotalPlotAjutangsho)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.KhatianShare)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.KhatianPlotAcre)}</td>");
                    sbRows.Append($"<td class=\"text-center\">{System.Net.WebUtility.HtmlEncode(row.KhatianPlotAjutangsho)}</td>");
                    sbRows.Append($"<td class=\"text-left\">{System.Net.WebUtility.HtmlEncode(row.Remarks)}</td>");
                    sbRows.Append("</tr>\n");
                }

                string printBtnHtml = UserSession.IsAdmin
                    ? "<button class=\"btn\" onclick=\"window.print()\">🖨️ প্রিন্ট করুন (Ctrl + P)</button>"
                    : "<span style=\"color:#64748b; font-size:13px; font-weight:bold;\">🔒 ভিউ মোড (শুধুমাত্র পরিদর্শন)</span>";

                string html = $@"<!DOCTYPE html>
<html lang=""bn"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>খতিয়ান নং {khatianNo} — বাংলাদেশ ফরম নং ৫৪৬২</title>
    <style>
        @page {{
            size: A4 landscape;
            margin: 10mm;
        }}
        body {{
            font-family: 'Kalpurush', 'SolaimanLipi', 'Nirmala UI', 'Vrinda', 'Segoe UI', Arial, sans-serif;
            background-color: #cbd5e1;
            margin: 0;
            padding: 20px;
            color: #0f172a;
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
        }}
        .no-print {{
            width: 277mm;
            margin: 0 auto 12px auto;
            display: flex;
            justify-content: space-between;
            align-items: center;
            background: #ffffff;
            padding: 10px 18px;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.08);
            border: 1px solid #94a3b8;
        }}
        .btn {{
            background-color: #047857;
            color: #ffffff;
            border: none;
            padding: 8px 18px;
            font-size: 14px;
            font-weight: bold;
            border-radius: 6px;
            cursor: pointer;
            transition: background 0.2s;
        }}
        .btn:hover {{ background-color: #065f46; }}
        .sheet {{
            background: #ffffff;
            width: 277mm;
            min-height: 190mm;
            margin: 0 auto;
            padding: 12mm 15mm;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
            box-sizing: border-box;
            border: 1px solid #475569;
        }}
        @media print {{
            body {{
                background: none;
                padding: 0;
            }}
            .sheet {{
                box-shadow: none;
                border: none;
                width: 100%;
                min-height: auto;
                padding: 0;
                margin: 0;
            }}
            .no-print {{
                display: none !important;
            }}
        }}
        .hdr-top {{
            display: flex;
            justify-content: space-between;
            font-size: 12px;
            font-weight: bold;
            color: #334155;
            margin-bottom: 2px;
        }}
        .title {{
            text-align: center;
            font-size: 24px;
            font-weight: bold;
            margin: 2px 0 6px 0;
            letter-spacing: 1px;
        }}
        .survey-kht {{
            display: flex;
            justify-content: space-between;
            font-size: 13.5px;
            font-weight: bold;
            margin-bottom: 6px;
        }}
        .admin-box {{
            border: 1px solid #334155;
            background: #f8fafc;
            display: grid;
            grid-template-columns: 1.3fr 1.3fr 1.8fr 2.2fr 1.3fr 1.1fr;
            padding: 5px 8px;
            font-size: 12px;
            margin-bottom: 6px;
        }}
        .tbl {{
            width: 100%;
            border-collapse: collapse;
            border: 1px solid #334155;
            font-size: 11px;
            table-layout: fixed;
        }}
        .tbl th, .tbl td {{
            border-right: 1px solid #334155;
            border-bottom: 0px solid transparent;
            padding: 4px 3px;
            word-wrap: break-word;
        }}
        .tbl th {{
            background-color: #f1f5f9;
            font-weight: bold;
            text-align: center;
            border-bottom: 1px solid #334155;
            vertical-align: middle;
        }}
        .tbl .col-num th {{
            background-color: #e2e8f0;
            font-size: 10.5px;
            padding: 2px;
            border-bottom: 1px solid #334155;
        }}
        .tbl tr.total-row td {{
            background-color: #f8fafc;
            border-top: 1px solid #334155;
            border-bottom: 1px solid #334155;
            font-weight: bold;
        }}
        .text-center {{ text-align: center; }}
        .text-left {{ text-align: left; }}
        .font-bold {{ font-weight: bold; }}
    </style>
</head>
<body>
    <div class=""no-print"">
        <div>
            <span style=""font-size: 16px; font-weight: bold; color: #0284c7;"">📜 বাংলাদেশ ফরম নং ৫৪৬২</span>
            <span style=""font-size: 14px; color: #334155; margin-left: 10px;"">খতিয়ান নং: <b>{khatianNo}</b> | মৌজা: <b>{mouza}</b></span>
        </div>
        <div>
            {printBtnHtml}
        </div>
    </div>

    <div class=""sheet"">
        <div class=""hdr-top"">
            <div>বাংলাদেশ ফরম নং ৫৪৬২</div>
            <div>পৃষ্ঠা নম্বরঃ ১/১</div>
        </div>

        <div class=""title"">খতিয়ান</div>

        <div class=""survey-kht"">
            <div>জরিপের নাম: <span>{surveyName}</span></div>
            <div>
                খতিয়ান নং: <span style=""font-size: 15px;"">{khatianNo}</span>
                &nbsp;&nbsp;&nbsp;&nbsp;সাবেক খতিয়ান নং: <span>{formerKhatian}</span>
            </div>
        </div>

        <div class=""admin-box"">
            <div><b>বিভাগ:</b> {division}</div>
            <div><b>জেলা:</b> {district}</div>
            <div><b>উপজেলা/থানা:</b> {upazila}</div>
            <div><b>মৌজা:</b> {mouza}</div>
            <div><b>জে. এল. নং:</b> {jlNo}</div>
            <div><b>শীট নং:</b> {sheetNo}</div>
        </div>

        <table class=""tbl"">
            <colgroup>
                <col style=""width: 230px;"">
                <col style=""width: 55px;"">
                <col style=""width: 75px;"">
                <col style=""width: 75px;"">
                <col style=""width: 68px;"">
                <col style=""width: 68px;"">
                <col style=""width: 75px;"">
                <col style=""width: 55px;"">
                <col style=""width: 65px;"">
                <col style=""width: 75px;"">
                <col style=""width: 55px;"">
                <col style=""width: 65px;"">
                <col style=""width: 104px;"">
            </colgroup>
            <thead>
                <tr>
                    <th rowspan=""2"">মালিকের নাম, পিতা, মাতা, স্বামী/স্ত্রীর নাম ও ঠিকানা, NID/জন্মনিবন্ধন</th>
                    <th rowspan=""2"">মালিকানা<br>অংশ</th>
                    <th colspan=""2"">সাবেক ও বর্তমান দাগ নম্বর</th>
                    <th colspan=""3"">জমির শ্রেণি</th>
                    <th colspan=""2"">দাগে মোট জমির পরিমাণ</th>
                    <th rowspan=""2"">দাগের মধ্যে এই<br>খতিয়ানের অংশ</th>
                    <th colspan=""2"">অংশানুযায়ী জমির পরিমাণ</th>
                    <th rowspan=""2"">দখল বিষয়ক বা অন্যান্য<br>বিশেষ মন্তব্য</th>
                </tr>
                <tr>
                    <th>সাবেক দাগ</th>
                    <th>বর্তমান দাগ</th>
                    <th>কৃষি</th>
                    <th>অকৃষি</th>
                    <th>স্থানীয় নাম</th>
                    <th>একর</th>
                    <th>অযুতাংশ</th>
                    <th>একর</th>
                    <th>অযুতাংশ</th>
                </tr>
                <tr class=""col-num"">
                    <th>১</th>
                    <th>২</th>
                    <th>৩(ক)</th>
                    <th>৩(খ)</th>
                    <th>৪(ক)</th>
                    <th>৪(খ)</th>
                    <th>৪(গ)</th>
                    <th>৫(ক)</th>
                    <th>৫(খ)</th>
                    <th>৬</th>
                    <th>৭(ক)</th>
                    <th>৭(খ)</th>
                    <th>৮</th>
                </tr>
            </thead>
            <tbody>
                {sbRows}
                <tr class=""total-row"">
                    <td class=""text-center font-bold"">মোট</td>
                    <td class=""text-center"">{totOwnerShare}</td>
                    <td colspan=""5""></td>
                    <td class=""text-center"">{totPlotAcre}</td>
                    <td class=""text-center"">{totPlotAju}</td>
                    <td></td>
                    <td class=""text-center"">{totKhtAcre}</td>
                    <td class=""text-center"">{totKhtAju}</td>
                    <td></td>
                </tr>
            </tbody>
        </table>
    </div>
</body>
</html>";

                string tempDir = Path.Combine(Path.GetTempPath(), "LandPressKhatianViews");
                Directory.CreateDirectory(tempDir);
                string tempFilePath = Path.Combine(tempDir, $"Khatian_{khatianNo}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                File.WriteAllText(tempFilePath, html, Encoding.UTF8);

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ব্রাউজার ভিউ তৈরি করার সময় ত্রুটি:\n{ex.Message}", "ব্রাউজার ভিউ ত্রুটি", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void SplitAcreAndAjutangsho(object? areaObj, out string acreStr, out string ajuStr)
        {
            acreStr = "০";
            ajuStr = "০০০০";
            if (areaObj == null || areaObj == DBNull.Value) return;

            string s = areaObj.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(s)) return;

            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal val) ||
                decimal.TryParse(s, out val))
            {
                long acre = (long)Math.Truncate(val);
                decimal remainder = Math.Abs(val - acre);
                long ajutangsho = (long)Math.Round(remainder * 10000m);
                acreStr = BanglaNumberConverter.ToBanglaDigit(acre.ToString());
                ajuStr = BanglaNumberConverter.ToBanglaDigit(ajutangsho.ToString("D4"));
            }
        }

        /// <summary>
        /// Renders the official Form 5462 Bangladesh Khatian (Porcha) layout
        /// strictly following DevResource\RptKhatian01dw - Copy.rdlc specifications.
        /// </summary>
        private void RenderKhatianReportView(DataRowView drv, DataView? dvOwners, DataView? dvPlots)
        {
            string currentKhatian = drv["CurrentKhatian"]?.ToString() ?? string.Empty;
            string bnCurrentKhatian = BanglaNumberConverter.ToBanglaDigit(currentKhatian);
            string bnFormerKhatian = BanglaNumberConverter.ToBanglaDigit(drv["FormerKhatian"]);
            string bnSheet = BanglaNumberConverter.ToBanglaDigit(drv["SheetNum"]);
            string mouzaName = drv["MouzaName"]?.ToString() ?? string.Empty;
            string surveyName = drv["SurveyName"]?.ToString() ?? string.Empty;

            // Badges & Headers
            txtKhatianViewHeroBadge.Text = $"খতিয়ান: {bnCurrentKhatian}";
            rptKhatianNo.Text = bnCurrentKhatian;
            rptFormerKhatianNo.Text = !string.IsNullOrWhiteSpace(bnFormerKhatian) ? bnFormerKhatian : "—";
            rptSheetNo.Text = !string.IsNullOrWhiteSpace(bnSheet) ? bnSheet : "১";
            rptSurveyName.Text = !string.IsNullOrWhiteSpace(surveyName) ? surveyName : "বি. এস.";
            rptMouza.Text = !string.IsNullOrWhiteSpace(mouzaName) ? mouzaName : "—";

            // Administrative Hierarchy
            if (cmbDivision.SelectedItem is AreaHierarchyItem divItem && !string.IsNullOrWhiteSpace(divItem.UnitNam))
                rptDivision.Text = divItem.UnitNam;
            else
                rptDivision.Text = "—";

            if (cmbDistrict.SelectedItem is AreaHierarchyItem distItem && !string.IsNullOrWhiteSpace(distItem.UnitNam))
                rptDistrict.Text = distItem.UnitNam;
            else
                rptDistrict.Text = "—";

            if (cmbUpazila.SelectedItem is AreaHierarchyItem upaItem && !string.IsNullOrWhiteSpace(upaItem.UnitNam))
                rptUpazila.Text = upaItem.UnitNam;
            else
                rptUpazila.Text = "—";

            if (cmbMouza.SelectedItem is AreaHierarchyItem mouzaItem)
            {
                if (!string.IsNullOrWhiteSpace(mouzaItem.UnitNam))
                    rptMouza.Text = mouzaItem.UnitNam;
                if (!string.IsNullOrWhiteSpace(mouzaItem.ResaNum))
                    rptJLNo.Text = BanglaNumberConverter.ToBanglaDigit(mouzaItem.ResaNum);
                else
                    rptJLNo.Text = "—";
            }
            else
            {
                rptJLNo.Text = "—";
            }

            // Build Form 5462 Rows (aligning Owners Col 1-2 with Plots Col 3-8 side-by-side)
            int ownerCount = dvOwners != null ? dvOwners.Count : 0;
            int plotCount = dvPlots != null ? dvPlots.Count : 0;
            int maxRows = Math.Max(ownerCount, plotCount);
            if (maxRows == 0) maxRows = 1;

            var reportRows = new List<KhatianReportRow>();
            decimal sumOwnerShare = 0m;
            decimal sumTotalPlotArea = 0m;
            decimal sumKhatianPlotArea = 0m;

            for (int i = 0; i < maxRows; i++)
            {
                var row = new KhatianReportRow();

                // Owner Info (Col 0 & 1)
                if (dvOwners != null && i < ownerCount)
                {
                    var oRow = dvOwners[i];
                    string oName = oRow["OwnerName"]?.ToString() ?? string.Empty;
                    string fName = oRow["FatherName"]?.ToString() ?? string.Empty;
                    string mName = oRow["MotherName"]?.ToString() ?? string.Empty;
                    string spName = oRow["SpouseName"]?.ToString() ?? string.Empty;
                    string addr = oRow["Address"]?.ToString() ?? string.Empty;
                    string nid = oRow["Nid"]?.ToString() ?? string.Empty;
                    string br = oRow["BirthReg"]?.ToString() ?? string.Empty;

                    var sbOwner = new StringBuilder();
                    sbOwner.Append(BanglaNumberConverter.ToBanglaDigit((i + 1).ToString())).Append(". ").Append(oName);
                    if (!string.IsNullOrWhiteSpace(fName)) sbOwner.Append(", পিতা: ").Append(fName);
                    if (!string.IsNullOrWhiteSpace(spName)) sbOwner.Append(", স্বামী/স্ত্রী: ").Append(spName);
                    if (!string.IsNullOrWhiteSpace(mName)) sbOwner.Append(", মাতা: ").Append(mName);
                    if (!string.IsNullOrWhiteSpace(addr)) sbOwner.Append(", ঠিকানা: ").Append(addr);
                    if (!string.IsNullOrWhiteSpace(nid)) sbOwner.Append(", NID: ").Append(BanglaNumberConverter.ToBanglaDigit(nid));
                    else if (!string.IsNullOrWhiteSpace(br)) sbOwner.Append(", জন্মনিবন্ধন: ").Append(BanglaNumberConverter.ToBanglaDigit(br));

                    row.OwnerDetails = sbOwner.ToString();

                    if (decimal.TryParse(oRow["OwnerShareArea"]?.ToString(), out decimal shareVal))
                    {
                        row.OwnerShare = BanglaNumberConverter.FormatBanglaDecimal(shareVal, "0.0000");
                        sumOwnerShare += shareVal;
                    }
                    else
                    {
                        row.OwnerShare = "০.০০০০";
                    }
                }

                // Plot Info (Col 2 to 12)
                if (dvPlots != null && i < plotCount)
                {
                    var pRow = dvPlots[i];
                    row.FormerPlot = BanglaNumberConverter.ToBanglaDigit(pRow["FormerPlot"]);
                    row.CurrentPlot = BanglaNumberConverter.ToBanglaDigit(pRow["CurrentPlot"]);
                    row.AgriClass_4Ka = pRow["AgriLandClass_4Ka"]?.ToString() ?? string.Empty;
                    row.NonAgriClass_4Kha = pRow["NonAgriLandClass_4Kha"]?.ToString() ?? string.Empty;
                    row.LocalName_4Ga = pRow["LocalName_4Ga"]?.ToString() ?? string.Empty;

                    SplitAcreAndAjutangsho(pRow["TotalPlotArea"], out string tAcre, out string tAju);
                    row.TotalPlotAcre = tAcre;
                    row.TotalPlotAjutangsho = tAju;
                    if (decimal.TryParse(pRow["TotalPlotArea"]?.ToString(), out decimal tAreaVal))
                        sumTotalPlotArea += tAreaVal;

                    if (decimal.TryParse(pRow["KhatianShare"]?.ToString(), out decimal kShareVal))
                        row.KhatianShare = BanglaNumberConverter.FormatBanglaDecimal(kShareVal, "0.0000");
                    else
                        row.KhatianShare = "০.০০০০";

                    SplitAcreAndAjutangsho(pRow["KhatianPlotArea"], out string kAcre, out string kAju);
                    row.KhatianPlotAcre = kAcre;
                    row.KhatianPlotAjutangsho = kAju;
                    if (decimal.TryParse(pRow["KhatianPlotArea"]?.ToString(), out decimal kAreaVal))
                        sumKhatianPlotArea += kAreaVal;

                    row.Remarks = pRow["PlotRemarks"]?.ToString() ?? string.Empty;
                }

                reportRows.Add(row);
            }

            icKhatianRows.ItemsSource = reportRows;
            _currentKhatianReportRows = reportRows;

            // Totals Row
            rptTotalOwnerShare.Text = BanglaNumberConverter.FormatBanglaDecimal(sumOwnerShare, "0.0000");
            SplitAcreAndAjutangsho(sumTotalPlotArea, out string totPltAcre, out string totPltAju);
            rptTotalPlotAcre.Text = totPltAcre;
            rptTotalPlotAjutangsho.Text = totPltAju;

            SplitAcreAndAjutangsho(sumKhatianPlotArea, out string totKhtAcre, out string totKhtAju);
            rptTotalKhatianAcre.Text = totKhtAcre;
            rptTotalKhatianAjutangsho.Text = totKhtAju;
        }

        #endregion
    }

    /// <summary>
    /// Data model representing a single combined row of Bangladesh Form 5462
    /// (DevResource\RptKhatian01dw - Copy.rdlc specification).
    /// </summary>
    public class KhatianReportRow
    {
        public string OwnerDetails { get; set; } = string.Empty;
        public string OwnerShare { get; set; } = string.Empty;
        public string FormerPlot { get; set; } = string.Empty;
        public string CurrentPlot { get; set; } = string.Empty;
        public string AgriClass_4Ka { get; set; } = string.Empty;
        public string NonAgriClass_4Kha { get; set; } = string.Empty;
        public string LocalName_4Ga { get; set; } = string.Empty;
        public string TotalPlotAcre { get; set; } = string.Empty;
        public string TotalPlotAjutangsho { get; set; } = string.Empty;
        public string KhatianShare { get; set; } = string.Empty;
        public string KhatianPlotAcre { get; set; } = string.Empty;
        public string KhatianPlotAjutangsho { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
    }
}
