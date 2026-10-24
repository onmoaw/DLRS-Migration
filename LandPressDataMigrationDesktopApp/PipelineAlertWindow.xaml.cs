using System.Windows;

namespace LandPressDataMigrationDesktopApp
{
    public enum PipelineAlertAction
    {
        Retry,
        Skip,
        Abort
    }

    /// <summary>
    /// Professional styled alert dialog for presenting errors, warnings, and failsafe choices during pipeline execution.
    /// </summary>
    public partial class PipelineAlertWindow : Window
    {
        public PipelineAlertAction UserAction { get; private set; } = PipelineAlertAction.Abort;

        public PipelineAlertWindow(
            string title,
            string stepName,
            string message,
            string technicalDetails,
            bool allowSkip = true)
        {
            InitializeComponent();

            txtAlertTitle.Text = title;
            txtStepName.Text = $"Location: {stepName}";
            txtUserMessage.Text = message;
            txtDetails.Text = string.IsNullOrWhiteSpace(technicalDetails) ? "(No additional error details available)" : technicalDetails;

            btnSkip.Visibility = allowSkip ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            UserAction = PipelineAlertAction.Retry;
            DialogResult = true;
            Close();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            UserAction = PipelineAlertAction.Skip;
            DialogResult = true;
            Close();
        }

        private void BtnAbort_Click(object sender, RoutedEventArgs e)
        {
            UserAction = PipelineAlertAction.Abort;
            DialogResult = false;
            Close();
        }

        public static (PipelineAlertAction Action, bool Confirmed) ShowAlert(
            Window owner,
            string title,
            string stepName,
            string message,
            string technicalDetails,
            bool allowSkip = true)
        {
            var dlg = new PipelineAlertWindow(title, stepName, message, technicalDetails, allowSkip)
            {
                Owner = owner
            };
            bool? res = dlg.ShowDialog();
            return (dlg.UserAction, res == true);
        }
    }
}
