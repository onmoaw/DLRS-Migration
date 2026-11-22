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

            // System Audit section visibility: ONLY for Admin users
            cardAuditSection.Visibility = UserSession.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

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
            if (_dsRecords != null && _dsRecords.Tables.Contains("Owners"))
            {
                DataTable dtOwners = _dsRecords.Tables["Owners"];
                var dvOwners = new DataView(dtOwners)
                {
                    RowFilter = $"CurrentKhatian = '{currentKhatian.Replace("'", "''")}'"
                };
                dgOwners.ItemsSource = dvOwners;
                txtOwnerCount.Text = $"মালিক সংখ্যা: {BanglaNumberConverter.ToBanglaDigit(dvOwners.Count)} জন";
            }

            // Section 3: Synchronize Plots Table (with dynamic 4-Ka, 4-Kha, 4-Ga)
            if (_dsRecords != null && _dsRecords.Tables.Contains("Plots"))
            {
                DataTable dtPlots = _dsRecords.Tables["Plots"];
                var dvPlots = new DataView(dtPlots)
                {
                    RowFilter = $"CurrentKhatian = '{currentKhatian.Replace("'", "''")}'"
                };
                dgPlots.ItemsSource = dvPlots;
                txtPlotCount.Text = $"দাগ সংখ্যা: {BanglaNumberConverter.ToBanglaDigit(dvPlots.Count)}টি";
            }

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
