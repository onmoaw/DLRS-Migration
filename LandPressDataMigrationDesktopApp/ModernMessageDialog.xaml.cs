using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace LandPressDataMigrationDesktopApp
{
    public enum ModernDialogKind
    {
        Success,
        Info,
        Warning,
        Error,
        Confirmation
    }

    public partial class ModernMessageDialog : Window
    {
        private readonly MessageBoxButton _buttonMode;
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public ModernMessageDialog(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            InitializeComponent();
            _buttonMode = buttons;

            txtTitle.Text = string.IsNullOrWhiteSpace(caption) ? "Notification" : caption;
            txtMessage.Text = message ?? string.Empty;

            var kind = ResolveKind(message ?? string.Empty, caption ?? string.Empty, buttons, icon);
            ApplyKindStyling(kind);
            ConfigureButtons(buttons);
        }

        private static ModernDialogKind ResolveKind(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            if (icon == MessageBoxImage.Error)
                return ModernDialogKind.Error;
            if (icon == MessageBoxImage.Warning)
                return ModernDialogKind.Warning;
            if (icon == MessageBoxImage.Question || buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel || buttons == MessageBoxButton.OKCancel)
                return ModernDialogKind.Confirmation;

            string combined = $"{caption} {message}";
            if (combined.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("ready", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("✔", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("সফল", StringComparison.OrdinalIgnoreCase))
            {
                return ModernDialogKind.Success;
            }

            return ModernDialogKind.Info;
        }

        private void ApplyKindStyling(ModernDialogKind kind)
        {
            switch (kind)
            {
                case ModernDialogKind.Success:
                    cardRoot.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(16, 185, 129));   // #10B981
                    badgeBg.Background = new SolidColorBrush(MediaColor.FromRgb(6, 95, 70));         // #065F46
                    txtIcon.Text = "✔";
                    txtIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(52, 211, 153));      // #34D399
                    txtTypeTag.Text = "SUCCESS";
                    txtTypeTag.Foreground = new SolidColorBrush(MediaColor.FromRgb(16, 185, 129));
                    btnPrimary.Background = CreateGradient(MediaColor.FromRgb(16, 185, 129), MediaColor.FromRgb(5, 150, 105));
                    break;

                case ModernDialogKind.Warning:
                    cardRoot.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(245, 158, 11));   // #F59E0B
                    badgeBg.Background = new SolidColorBrush(MediaColor.FromRgb(120, 53, 15));       // #78350F
                    txtIcon.Text = "⚠";
                    txtIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(251, 191, 36));      // #FBBF24
                    txtTypeTag.Text = "WARNING";
                    txtTypeTag.Foreground = new SolidColorBrush(MediaColor.FromRgb(245, 158, 11));
                    btnPrimary.Background = CreateGradient(MediaColor.FromRgb(245, 158, 11), MediaColor.FromRgb(217, 119, 6));
                    break;

                case ModernDialogKind.Error:
                    cardRoot.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(239, 68, 68));    // #EF4444
                    badgeBg.Background = new SolidColorBrush(MediaColor.FromRgb(127, 29, 29));       // #7F1D1D
                    txtIcon.Text = "✖";
                    txtIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(248, 113, 113));     // #F87171
                    txtTypeTag.Text = "ERROR";
                    txtTypeTag.Foreground = new SolidColorBrush(MediaColor.FromRgb(239, 68, 68));
                    btnPrimary.Background = CreateGradient(MediaColor.FromRgb(239, 68, 68), MediaColor.FromRgb(220, 38, 38));
                    break;

                case ModernDialogKind.Confirmation:
                    cardRoot.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(56, 189, 248));   // #38BDF8
                    badgeBg.Background = new SolidColorBrush(MediaColor.FromRgb(12, 74, 110));       // #0C4A6E
                    txtIcon.Text = "?";
                    txtIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(56, 189, 248));      // #38BDF8
                    txtTypeTag.Text = "CONFIRMATION";
                    txtTypeTag.Foreground = new SolidColorBrush(MediaColor.FromRgb(56, 189, 248));
                    btnPrimary.Background = CreateGradient(MediaColor.FromRgb(59, 130, 246), MediaColor.FromRgb(37, 99, 235));
                    break;

                default: // Info
                    cardRoot.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(2, 132, 199));    // #0284C7
                    badgeBg.Background = new SolidColorBrush(MediaColor.FromRgb(12, 74, 110));       // #0C4A6E
                    txtIcon.Text = "ℹ";
                    txtIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(56, 189, 248));      // #38BDF8
                    txtTypeTag.Text = "INFORMATION";
                    txtTypeTag.Foreground = new SolidColorBrush(MediaColor.FromRgb(2, 132, 199));
                    btnPrimary.Background = CreateGradient(MediaColor.FromRgb(59, 130, 246), MediaColor.FromRgb(37, 99, 235));
                    break;
            }
        }

        private static LinearGradientBrush CreateGradient(MediaColor c1, MediaColor c2)
        {
            return new LinearGradientBrush(c1, c2, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        }

        private void ConfigureButtons(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.YesNo:
                    btnPrimary.Content = "Yes";
                    btnSecondary.Content = "No";
                    btnSecondary.Visibility = Visibility.Visible;
                    btnSecondary.IsCancel = true;
                    Result = MessageBoxResult.No;
                    break;

                case MessageBoxButton.YesNoCancel:
                    btnPrimary.Content = "Yes";
                    btnSecondary.Content = "No";
                    btnSecondary.Visibility = Visibility.Visible;
                    btnCancel.Content = "Cancel";
                    btnCancel.Visibility = Visibility.Visible;
                    btnCancel.IsCancel = true;
                    Result = MessageBoxResult.Cancel;
                    break;

                case MessageBoxButton.OKCancel:
                    btnPrimary.Content = "OK";
                    btnSecondary.Content = "Cancel";
                    btnSecondary.Visibility = Visibility.Visible;
                    btnSecondary.IsCancel = true;
                    Result = MessageBoxResult.Cancel;
                    break;

                default: // OK
                    btnPrimary.Content = "OK";
                    btnPrimary.IsCancel = true;
                    Result = MessageBoxResult.OK;
                    break;
            }
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            Result = (_buttonMode == MessageBoxButton.YesNo || _buttonMode == MessageBoxButton.YesNoCancel)
                ? MessageBoxResult.Yes
                : MessageBoxResult.OK;
            DialogResult = true;
            Close();
        }

        private void BtnSecondary_Click(object sender, RoutedEventArgs e)
        {
            Result = (_buttonMode == MessageBoxButton.OKCancel)
                ? MessageBoxResult.Cancel
                : MessageBoxResult.No;
            DialogResult = false;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            DialogResult = false;
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

    public static class ModernMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
            => Show(null, messageBoxText, "Notification", MessageBoxButton.OK, MessageBoxImage.Information);

        public static MessageBoxResult Show(string messageBoxText, string caption)
            => Show(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.Information);

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
            => Show(null, messageBoxText, caption, button, MessageBoxImage.Information);

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
            => Show(null, messageBoxText, caption, button, icon);

        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            var app = System.Windows.Application.Current;
            if (app == null)
                return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon);

            if (!app.Dispatcher.CheckAccess())
            {
                return app.Dispatcher.Invoke(() => Show(owner, messageBoxText, caption, button, icon));
            }

            var resolvedOwner = owner
                ?? app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
                ?? app.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);

            var dlg = new ModernMessageDialog(messageBoxText, caption, button, icon);
            if (resolvedOwner != null && resolvedOwner != dlg)
            {
                dlg.Owner = resolvedOwner;
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dlg.ShowDialog();
            return dlg.Result;
        }
    }
}
