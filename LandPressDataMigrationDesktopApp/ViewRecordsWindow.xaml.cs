using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Data model representing a Land Record (Khatian, Dag, Owner, Area) with demo/dummy data.
    /// </summary>
    public class LandRecordDummyModel
    {
        public int SlNo { get; set; }
        public string Division { get; set; } = "সিলেট";
        public string District { get; set; } = "সিলেট";
        public string Upazila { get; set; } = "সিলেট সদর";
        public string Mouza { get; set; } = "আম্বরখানা (০১)";
        public string SurveyType { get; set; } = "বি.এস. (BS)";
        public string CurrentKhatian { get; set; } = "";
        public string FormerKhatian { get; set; } = "";
        public string CurrentPlot { get; set; } = "";
        public string FormerPlot { get; set; } = "";
        public string CsPlot { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string FatherName { get; set; } = "";
        public string MotherName { get; set; } = "";
        public string SpouseName { get; set; } = "";
        public string Nid { get; set; } = "";
        public string BirthReg { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Address { get; set; } = "";
        public string LandClass { get; set; } = "নাল";
        public string LandAreaAcre { get; set; } = "০.১২৫০";
        public string Share { get; set; } = "০.২৫০০";
    }

    /// <summary>
    /// Interaction logic for ViewRecordsWindow.xaml
    /// Pop-up screen featuring all land search/filter fields from the provided specification.
    /// </summary>
    public partial class ViewRecordsWindow : Window
    {
        private List<LandRecordDummyModel> _allDummyRecords = new List<LandRecordDummyModel>();

        public ViewRecordsWindow()
        {
            InitializeComponent();
            Loaded += ViewRecordsWindow_Loaded;
            Unloaded += ViewRecordsWindow_Unloaded;
        }

        private void ViewRecordsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ThemeChanged += OnThemeChanged;
            UpdateThemeButton();

            // Initialize rich realistic dummy land records
            SeedDummyRecords();

            // Populate the grid with all records by default
            FilterAndDisplayRecords();
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

        /// <summary>
        /// Populates a comprehensive list of dummy records with authentic Bengali land data.
        /// </summary>
        private void SeedDummyRecords()
        {
            _allDummyRecords = new List<LandRecordDummyModel>
            {
                new LandRecordDummyModel
                {
                    SlNo = 1,
                    Division = "সিলেট",
                    District = "সিলেট",
                    Upazila = "সিলেট সদর",
                    Mouza = "আম্বরখানা (০১)",
                    SurveyType = "বি.এস. (BS)",
                    CurrentKhatian = "১০৫",
                    FormerKhatian = "৪৫",
                    CurrentPlot = "১২০৪",
                    FormerPlot = "৮৫০",
                    CsPlot = "৩১২",
                    OwnerName = "মো: রফিকুল ইসলাম",
                    FatherName = "মরহুম আব্দুল জলিল",
                    MotherName = "আমেনা বেগম",
                    SpouseName = "ফাতেমা খাতুন",
                    Nid = "১৯৮৫৯১১২৩৪৫৬৭৮৯০১",
                    BirthReg = "১৯৮৫৯১১০০০১২৩৪৫৬৭",
                    Mobile = "০১৭xxxxxxxx",
                    Address = "গ্রাম: আম্বরখানা, ডাকঘর: সিলেট-৩১০০",
                    LandClass = "বাড়ি",
                    LandAreaAcre = "০.০৮৫০ একর",
                    Share = "০.৫০০০"
                },
                new LandRecordDummyModel
                {
                    SlNo = 2,
                    Division = "সিলেট",
                    District = "সিলেট",
                    Upazila = "সিলেট সদর",
                    Mouza = "আম্বরখানা (০১)",
                    SurveyType = "বি.এস. (BS)",
                    CurrentKhatian = "১০৫",
                    FormerKhatian = "৪৫",
                    CurrentPlot = "১২০৪",
                    FormerPlot = "৮৫০",
                    CsPlot = "৩১২",
                    OwnerName = "মোসা: ফাতেমা খাতুন",
                    FatherName = "মো: শামসুল হক",
                    MotherName = "জাহানারা বেগম",
                    SpouseName = "মো: রফিকুল ইসলাম",
                    Nid = "১৯৮৮৯১১২৩৪৫৬৭৮৯১০",
                    BirthReg = "১৯৮৮৯১১০০০১২৩৪৫৮৮",
                    Mobile = "০১৮xxxxxxxx",
                    Address = "গ্রাম: আম্বরখানা, ডাকঘর: সিলেট-৩১০০",
                    LandClass = "বাড়ি",
                    LandAreaAcre = "০.০৮৫০ একর",
                    Share = "০.৫০০০"
                },
                new LandRecordDummyModel
                {
                    SlNo = 3,
                    Division = "সিলেট",
                    District = "সিলেট",
                    Upazila = "সিলেট সদর",
                    Mouza = "শিবগঞ্জ (০৪)",
                    SurveyType = "বি.এস. (BS)",
                    CurrentKhatian = "২১০",
                    FormerKhatian = "৮৯",
                    CurrentPlot = "১৫৬২",
                    FormerPlot = "৯২০",
                    CsPlot = "৪১৫",
                    OwnerName = "কামাল উদ্দিন চৌধুরী",
                    FatherName = "নাসির উদ্দিন চৌধুরী",
                    MotherName = "রোকেয়া চৌধুরী",
                    SpouseName = "নাজমা বেগম",
                    Nid = "১৯৭৫৯১১২৩৪৫৬৭৮৯২২",
                    BirthReg = "১৯৭৫৯১১০০০১২৩৪৫৯৯",
                    Mobile = "০১৯xxxxxxxx",
                    Address = "গ্রাম: শিবগঞ্জ, ডাকঘর: সিলেট সদর",
                    LandClass = "নাল",
                    LandAreaAcre = "০.২৫০০ একর",
                    Share = "১.০০০০"
                },
                new LandRecordDummyModel
                {
                    SlNo = 4,
                    Division = "সিলেট",
                    District = "সিলেট",
                    Upazila = "সিলেট সদর",
                    Mouza = "লামাবাজার (০২)",
                    SurveyType = "বি.এস. (BS)",
                    CurrentKhatian = "৩১৫",
                    FormerKhatian = "১১০",
                    CurrentPlot = "১৮৪০",
                    FormerPlot = "১০১৫",
                    CsPlot = "৫৫০",
                    OwnerName = "সৈয়দ আশরাফ আলী",
                    FatherName = "সৈয়দ মনসুর আলী",
                    MotherName = "সৈয়দা সেলিনা বানু",
                    SpouseName = "পারভীন সুলতানা",
                    Nid = "১৯৮০৯১১২৩৪৫৬৭৮৯৩৩",
                    BirthReg = "১৯৮০৯১১০০০১২৩৪৬১১",
                    Mobile = "০১৬xxxxxxxx",
                    Address = "গ্রাম: লামাবাজার, ডাকঘর: সিলেট-৩১০০",
                    LandClass = "দোকান",
                    LandAreaAcre = "০.০৪০০ একর",
                    Share = "০.৭৫০০"
                },
                new LandRecordDummyModel
                {
                    SlNo = 5,
                    Division = "সিলেট",
                    District = "সিলেট",
                    Upazila = "সিলেট সদর",
                    Mouza = "উপশহর (০৩)",
                    SurveyType = "বি.এস. (BS)",
                    CurrentKhatian = "৪২০",
                    FormerKhatian = "১৮৫",
                    CurrentPlot = "২২১০",
                    FormerPlot = "১২৪৫",
                    CsPlot = "৬৭০",
                    OwnerName = "মাহবুবুর রহমান",
                    FatherName = "তোফাজ্জল হোসেন",
                    MotherName = "মমতাজ বেগম",
                    SpouseName = "রুমানা আক্তার",
                    Nid = "১৯৯১৯১১২৩৪৫৬৭৮৯৪৪",
                    BirthReg = "১৯৯১৯১১০০০১২৩৪৬২২",
                    Mobile = "০১৫xxxxxxxx",
                    Address = "গ্রাম: উপশহর, ব্লক-সি, সিলেট",
                    LandClass = "ভিটি",
                    LandAreaAcre = "০.১৫০০ একর",
                    Share = "১.০০০০"
                },
                new LandRecordDummyModel
                {
                    SlNo = 6,
                    Division = "সিলেট",
                    District = "সিলেট",
                    Upazila = "সিলেট সদর",
                    Mouza = "পাঠানটুলা (০৫)",
                    SurveyType = "বি.এস. (BS)",
                    CurrentKhatian = "৫২৫",
                    FormerKhatian = "২২০",
                    CurrentPlot = "২৫৩০",
                    FormerPlot = "১৪৩০",
                    CsPlot = "৭৮০",
                    OwnerName = "মো: আনোয়ার হোসেন",
                    FatherName = "মো: খলিলুর রহমান",
                    MotherName = "হাফেজা বেগম",
                    SpouseName = "শাহিদা আক্তার",
                    Nid = "১৯৮২৯১১২৩৪৫৬৭৮৯৫৫",
                    BirthReg = "১৯৮২৯১১০০০১২৩৪৬৩৩",
                    Mobile = "০১৭xxxxxxxx",
                    Address = "গ্রাম: পাঠানটুলা, সিলেট সদর",
                    LandClass = "পুকুর",
                    LandAreaAcre = "০.৩২০০ একর",
                    Share = "০.৫০০০"
                }
            };
        }

        /// <summary>
        /// Executes filtering against the dummy data based on all input fields.
        /// </summary>
        private void BtnShowResults_Click(object sender, RoutedEventArgs e)
        {
            FilterAndDisplayRecords();
        }

        private void FilterAndDisplayRecords()
        {
            string division = GetSelectedComboValue(cmbDivision);
            string district = GetSelectedComboValue(cmbDistrict);
            string survey = GetSelectedComboValue(cmbSurvey);
            string upazila = GetSelectedComboValue(cmbUpazila);
            string mouza = GetSelectedComboValue(cmbMouza);

            string curKhatian = txtCurrentKhatian.Text.Trim();
            string formerKhatian = txtFormerKhatian.Text.Trim();
            string curPlot = txtCurrentPlot.Text.Trim();
            string formerPlot = txtFormerPlot.Text.Trim();
            string csPlot = txtCsPlot.Text.Trim();

            string owner = txtOwner.Text.Trim();
            string father = txtFather.Text.Trim();
            string mother = txtMother.Text.Trim();
            string spouse = txtSpouse.Text.Trim();
            string nid = txtNid.Text.Trim();
            string birthReg = txtBirthReg.Text.Trim();
            string mobile = txtMobile.Text.Trim();
            string address = txtOwnerAddress.Text.Trim();

            var query = _allDummyRecords.AsEnumerable();

            if (!string.IsNullOrEmpty(division) && division != "নির্বাচন করুন")
                query = query.Where(r => r.Division.Contains(division, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(district) && district != "নির্বাচন করুন")
                query = query.Where(r => r.District.Contains(district, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(survey) && survey != "নির্বাচন করুন")
                query = query.Where(r => r.SurveyType.Contains(survey, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(upazila) && upazila != "নির্বাচন করুন")
                query = query.Where(r => r.Upazila.Contains(upazila, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(mouza) && mouza != "নির্বাচন করুন")
                query = query.Where(r => r.Mouza.Contains(mouza, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(curKhatian))
                query = query.Where(r => r.CurrentKhatian.Contains(curKhatian, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(formerKhatian))
                query = query.Where(r => r.FormerKhatian.Contains(formerKhatian, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(curPlot))
                query = query.Where(r => r.CurrentPlot.Contains(curPlot, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(formerPlot))
                query = query.Where(r => r.FormerPlot.Contains(formerPlot, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(csPlot))
                query = query.Where(r => r.CsPlot.Contains(csPlot, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(owner))
                query = query.Where(r => r.OwnerName.Contains(owner, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(father))
                query = query.Where(r => r.FatherName.Contains(father, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(mother))
                query = query.Where(r => r.MotherName.Contains(mother, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(spouse))
                query = query.Where(r => r.SpouseName.Contains(spouse, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(nid))
                query = query.Where(r => r.Nid.Contains(nid, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(birthReg))
                query = query.Where(r => r.BirthReg.Contains(birthReg, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(mobile))
                query = query.Where(r => r.Mobile.Contains(mobile, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(address))
                query = query.Where(r => r.Address.Contains(address, StringComparison.OrdinalIgnoreCase));

            var filteredList = query.ToList();

            dgRecords.ItemsSource = filteredList;
            txtRecordCount.Text = $"মোট রেকর্ড: {filteredList.Count} টি";

            if (filteredList.Count > 0)
            {
                dgRecords.SelectedIndex = 0;
            }
            else
            {
                txtSelectedRecordDetail.Text = "কোনো রেকর্ড পাওয়া যায়নি (ফিল্টার পরিবর্তন করে পুনরায় অনুসন্ধান করুন)";
            }
        }

        private string GetSelectedComboValue(ComboBox cmb)
        {
            if (cmb?.SelectedItem is ComboBoxItem item)
            {
                return item.Content?.ToString() ?? "";
            }
            return cmb?.Text ?? "";
        }

        /// <summary>
        /// Clears all search inputs and restores full demo list.
        /// </summary>
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            cmbDivision.SelectedIndex = 0;
            cmbDistrict.SelectedIndex = 0;
            cmbSurvey.SelectedIndex = 0;
            cmbUpazila.SelectedIndex = 0;
            cmbMouza.SelectedIndex = 0;

            txtCurrentKhatian.Text = "";
            txtFormerKhatian.Text = "";
            txtCurrentPlot.Text = "";
            txtFormerPlot.Text = "";
            txtCsPlot.Text = "";

            txtOwner.Text = "";
            txtFather.Text = "";
            txtMother.Text = "";
            txtSpouse.Text = "";
            txtNid.Text = "";
            txtBirthReg.Text = "";
            txtMobile.Text = "";
            txtOwnerAddress.Text = "";

            FilterAndDisplayRecords();
        }

        /// <summary>
        /// Automatically fills in demo search inputs so the user can test the UI immediately.
        /// </summary>
        private void BtnFillDemoData_Click(object sender, RoutedEventArgs e)
        {
            SetComboByText(cmbDivision, "সিলেট");
            SetComboByText(cmbDistrict, "সিলেট");
            SetComboByText(cmbSurvey, "বি.এস. (BS)");
            SetComboByText(cmbUpazila, "সিলেট সদর");
            SetComboByText(cmbMouza, "আম্বরখানা (০১)");

            txtCurrentKhatian.Text = "১০৫";
            txtCurrentPlot.Text = "১২০৪";
            txtOwner.Text = "রফিকুল";

            FilterAndDisplayRecords();
        }

        private void SetComboByText(ComboBox cmb, string text)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is ComboBoxItem cbi && cbi.Content?.ToString() == text)
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
        }

        private void DgRecords_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgRecords.SelectedItem is LandRecordDummyModel selected)
            {
                txtSelectedRecordDetail.Text =
                    $"খতিয়ান: {selected.CurrentKhatian} (সাবেক: {selected.FormerKhatian}) | " +
                    $"দাগ: {selected.CurrentPlot} (সাবেক: {selected.FormerPlot}, সি.এস: {selected.CsPlot}) | " +
                    $"মালিক: {selected.OwnerName} | পিতা: {selected.FatherName} | " +
                    $"শ্রেণী: {selected.LandClass} | পরিমাণ: {selected.LandAreaAcre} | অংশ: {selected.Share}";
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (dgRecords.ItemsSource is IEnumerable<LandRecordDummyModel> items)
            {
                var sb = new StringBuilder();
                sb.AppendLine("SlNo,Division,District,Upazila,Mouza,SurveyType,CurrentKhatian,FormerKhatian,CurrentPlot,FormerPlot,CsPlot,OwnerName,FatherName,MotherName,SpouseName,Nid,BirthReg,Mobile,Address,LandClass,LandAreaAcre,Share");

                foreach (var r in items)
                {
                    sb.AppendLine($"\"{r.SlNo}\",\"{r.Division}\",\"{r.District}\",\"{r.Upazila}\",\"{r.Mouza}\",\"{r.SurveyType}\",\"{r.CurrentKhatian}\",\"{r.FormerKhatian}\",\"{r.CurrentPlot}\",\"{r.FormerPlot}\",\"{r.CsPlot}\",\"{r.OwnerName}\",\"{r.FatherName}\",\"{r.MotherName}\",\"{r.SpouseName}\",\"{r.Nid}\",\"{r.BirthReg}\",\"{r.Mobile}\",\"{r.Address}\",\"{r.LandClass}\",\"{r.LandAreaAcre}\",\"{r.Share}\"");
                }

                try
                {
                    Clipboard.SetText(sb.ToString());
                    MessageBox.Show(
                        "রেকর্ডসমূহ সফলভাবে ক্লিপবোর্ডে কপি হয়েছে (CSV ফরম্যাটে)। আপনি এক্সেল বা নোটপ্যাডে পেস্ট করতে পারেন।",
                        "CSV Export Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("ক্লিপবোর্ডে কপি করতে সমস্যা হয়েছে: " + ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
