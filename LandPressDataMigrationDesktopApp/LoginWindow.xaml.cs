using System;
using System.Security.Cryptography;
using System.Text;
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
        // Application authentication salt (prevents dictionary/rainbow-table attacks)
        private static readonly byte[] AuthSalt =
        [
            0x44, 0x4C, 0x52, 0x53, 0x5F, 0x4D, 0x49, 0x47,
            0x52, 0x41, 0x54, 0x49, 0x4F, 0x4E, 0x5F, 0x53,
            0x41, 0x4C, 0x54, 0x5F, 0x32, 0x30, 0x32, 0x36
        ]; // "DLRS_MIGRATION_SALT_2026"

        // Salted SHA-256 byte hashes (plaintext credentials are never stored in binary metadata)
        private static readonly byte[] AdminUserHash =
        [
            0x1C, 0x97, 0xFF, 0x83, 0x8D, 0x44, 0x0A, 0x44,
            0x56, 0xEF, 0x4F, 0x7B, 0x56, 0x75, 0x5A, 0x1C,
            0xA7, 0x45, 0x8E, 0xF0, 0xBB, 0xF7, 0x67, 0xC7,
            0x1F, 0x1B, 0xCC, 0x67, 0x40, 0x75, 0xD6, 0x4D
        ];

        private static readonly byte[] AdminPasswordHash =
        [
            0x31, 0x8D, 0xA8, 0xFA, 0x23, 0x9D, 0xB0, 0xD3,
            0x43, 0xDB, 0xD3, 0xD0, 0x13, 0xE6, 0x77, 0x78,
            0xAB, 0x68, 0x77, 0x9D, 0x04, 0x74, 0xDE, 0x06,
            0xC2, 0x11, 0x49, 0xA5, 0xFD, 0x4F, 0x5A, 0xBE
        ];

        private static readonly byte[] StandardUserHash =
        [
            0xF3, 0x45, 0x86, 0xC6, 0x55, 0xE9, 0x38, 0xCD,
            0x79, 0x07, 0xEA, 0x0A, 0xCA, 0xC6, 0x4F, 0x6F,
            0x87, 0x33, 0xB5, 0x14, 0x61, 0xEA, 0x95, 0xDB,
            0x32, 0xB9, 0x92, 0x7E, 0x7F, 0x98, 0xDA, 0x7D
        ];

        private static readonly byte[] StandardPasswordHash =
        [
            0x26, 0xB7, 0x52, 0x64, 0x7C, 0xC4, 0x31, 0xBD,
            0x18, 0x87, 0x13, 0xAF, 0x9B, 0x42, 0xDB, 0x4E,
            0xF8, 0x5C, 0x6F, 0xDC, 0xBB, 0xBC, 0xE0, 0x49,
            0x42, 0xB0, 0x89, 0xF1, 0x3E, 0x74, 0x54, 0x29
        ];

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

            // Normalize username to lowercase for case-insensitive verification
            string normalizedUsername = username.ToLowerInvariant();

            // 1. Check Administrator credentials
            if (VerifySaltedHash(normalizedUsername, AdminUserHash) &&
                VerifySaltedHash(password, AdminPasswordHash))
            {
                UserSession.SetUser("Admin", UserRole.Admin);
                OpenMainApp();
                return;
            }

            // 2. Check Standard User credentials
            if (VerifySaltedHash(normalizedUsername, StandardUserHash) &&
                VerifySaltedHash(password, StandardPasswordHash))
            {
                UserSession.SetUser("User", UserRole.StandardUser);
                OpenMainApp();
                return;
            }

            ShowError("Invalid username or password. Please check your credentials.");
            txtPassword.SelectAll();
            txtPassword.Focus();
        }

        /// <summary>
        /// Computes SHA-256 hash of (AuthSalt + input) and verifies in constant time against the expected hash.
        /// Prevents plaintext credential leakage in reverse-engineered assemblies and timing attacks.
        /// </summary>
        private static bool VerifySaltedHash(string input, byte[] expectedHash)
        {
            if (string.IsNullOrEmpty(input) || expectedHash == null || expectedHash.Length != 32)
                return false;

            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] combined = new byte[AuthSalt.Length + inputBytes.Length];
            Buffer.BlockCopy(AuthSalt, 0, combined, 0, AuthSalt.Length);
            Buffer.BlockCopy(inputBytes, 0, combined, AuthSalt.Length, inputBytes.Length);

            using var sha256 = SHA256.Create();
            byte[] computedHash = sha256.ComputeHash(combined);

            return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
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
