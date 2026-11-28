using System.Windows;
using System.Windows.Input;

namespace LandPressDataMigrationDesktopApp
{
    public partial class DatabaseReadyDialog : Window
    {
        public DatabaseReadyDialog(string databaseName, string serverName)
        {
            InitializeComponent();
            txtDbName.Text = databaseName;
            txtServerName.Text = string.IsNullOrWhiteSpace(serverName) ? "-" : serverName;
            txtSubtitle.Text = $"Permanent Intermediate Database [{databaseName}] has been created and verified.";
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
