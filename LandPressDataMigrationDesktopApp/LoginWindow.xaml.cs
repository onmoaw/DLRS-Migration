using System;
using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        private const string AdminUser = "Admin";
        private const string AdminPassword = "Admin#321";

        private const string StandardUser = "User";
        private const string StandardPassword = "1234";

        public LoginWindow()
        {
            InitializeComponent();
            txtUsername.Focus();
            UpdateThemeButton();
            ThemeManager.ThemeChanged += OnThemeChanged;
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

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            PerformLogin();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformLogin();
            }
        }

        private void PerformLogin()
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrEmpty(username))
            {
                ShowError("Please enter your username.");
                txtUsername.Focus();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowError("Please enter your password.");
                txtPassword.Focus();
                return;
            }

            // 1. Check Administrator credentials
            if (string.Equals(username, AdminUser, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(password, AdminPassword, StringComparison.Ordinal))
            {
                UserSession.SetUser("Admin", UserRole.Admin);
                OpenMainApp();
                return;
            }

            // 2. Check Standard User credentials (User / 1234)
            if (string.Equals(username, StandardUser, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(password, StandardPassword, StringComparison.Ordinal))
            {
                UserSession.SetUser("User", UserRole.StandardUser);
                OpenMainApp();
                return;
            }

            ShowError("Invalid username or password. Please check your credentials.");
            txtPassword.SelectAll();
            txtPassword.Focus();
        }

        private void OpenMainApp()
        {
            txtError.Visibility = Visibility.Collapsed;
            ThemeManager.ThemeChanged -= OnThemeChanged;

            var mainWindow = new MainWindow();
            System.Windows.Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            this.Close();
        }

        private void ShowError(string message)
        {
            txtError.Text = $"⚠ {message}";
            txtError.Visibility = Visibility.Visible;
        }
    }
}
