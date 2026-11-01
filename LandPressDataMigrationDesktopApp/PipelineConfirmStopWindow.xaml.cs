using System.Windows;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Phase-specific confirmation dialog for stopping running operations or closing the app during execution.
    /// Default focus is on 'Continue Running' to prevent accidental aborts.
    /// </summary>
    public partial class PipelineConfirmStopWindow : Window
    {
        public bool IsConfirmed { get; private set; } = false;

        public PipelineConfirmStopWindow(
            string phaseName,
            string message,
            string? technicalDetail = null,
            bool isAppClosing = false)
        {
            InitializeComponent();

            if (isAppClosing)
            {
                txtDialogTitle.Text = "Confirm Application Exit";
                txtPhaseBadge.Text = $"Operation Running: {phaseName}";
                txtWarningNotice.Text = "An operation is currently in progress. Exiting will cancel running processes and roll back uncommitted work.";
                txtPhaseConsequence.Text = message;
                btnConfirmStop.Content = "Yes, Stop & Exit";
                btnContinue.Content = "Keep Running";
            }
            else
            {
                txtDialogTitle.Text = "Confirm Stop Operation";
                txtPhaseBadge.Text = $"Current Phase: {phaseName}";
                txtWarningNotice.Text = "Are you sure you want to stop the current operation?";
                txtPhaseConsequence.Text = message;
                btnConfirmStop.Content = "Yes, Stop Operation";
                btnContinue.Content = "Continue Running";
            }

            if (UserSession.IsAdmin && !string.IsNullOrWhiteSpace(technicalDetail))
            {
                panelTechDetails.Visibility = Visibility.Visible;
                txtTechDetails.Text = technicalDetail;
            }
            else
            {
                panelTechDetails.Visibility = Visibility.Collapsed;
            }

            Loaded += (s, e) => btnContinue.Focus();
        }

        private void BtnConfirmStop_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }

        public static bool ConfirmStop(
            Window owner,
            string phaseName,
            string message,
            string? technicalDetail = null,
            bool isAppClosing = false)
        {
            var dlg = new PipelineConfirmStopWindow(phaseName, message, technicalDetail, isAppClosing)
            {
                Owner = owner
            };
            dlg.ShowDialog();
            return dlg.IsConfirmed;
        }
    }
}
