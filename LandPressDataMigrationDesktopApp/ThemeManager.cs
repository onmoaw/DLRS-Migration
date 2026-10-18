using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WpfApp = System.Windows.Application;

namespace LandPressDataMigrationDesktopApp
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    /// <summary>
    /// Central manager for high-contrast Dark and Light themes across the entire application.
    /// </summary>
    public static class ThemeManager
    {
        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        public static event Action<AppTheme>? ThemeChanged;

        // ── Color Definitions ──
        public static class DarkColors
        {
            public static readonly MediaColor WindowBg = MediaColor.FromRgb(15, 23, 42);          // #0F172A
            public static readonly MediaColor CardBg = MediaColor.FromRgb(30, 41, 59);            // #1E293B
            public static readonly MediaColor CardBorder = MediaColor.FromRgb(51, 65, 85);        // #334155
            public static readonly MediaColor InsetBg = MediaColor.FromRgb(15, 23, 42);           // #0F172A
            public static readonly MediaColor InsetBorder = MediaColor.FromRgb(51, 65, 85);       // #334155
            public static readonly MediaColor InputBg = MediaColor.FromRgb(15, 23, 42);           // #0F172A
            public static readonly MediaColor InputBorder = MediaColor.FromRgb(51, 65, 85);       // #334155
            public static readonly MediaColor TextPrimary = MediaColor.FromRgb(248, 250, 252);    // #F8FAFC
            public static readonly MediaColor TextSecondary = MediaColor.FromRgb(148, 163, 184);  // #94A3B8
            public static readonly MediaColor TextMuted = MediaColor.FromRgb(100, 116, 139);      // #64748B
            public static readonly MediaColor HeaderBg = MediaColor.FromRgb(30, 41, 59);          // #1E293B
            public static readonly MediaColor FooterBg = MediaColor.FromRgb(10, 15, 29);          // #0A0F1D
            public static readonly MediaColor LockBg = MediaColor.FromRgb(59, 18, 25);            // #3B1219
            public static readonly MediaColor LockBorder = MediaColor.FromRgb(239, 68, 68);       // #EF4444
            public static readonly MediaColor LockText = MediaColor.FromRgb(252, 165, 165);       // #FCA5A5
            public static readonly MediaColor SuccessBg = MediaColor.FromRgb(6, 78, 59);          // #064E3B
            public static readonly MediaColor SuccessBorder = MediaColor.FromRgb(16, 185, 129);   // #10B981
            public static readonly MediaColor SuccessText = MediaColor.FromRgb(167, 243, 208);    // #A7F3D0
            public static readonly MediaColor WarningBg = MediaColor.FromRgb(69, 26, 3);          // #451A03
            public static readonly MediaColor WarningBorder = MediaColor.FromRgb(245, 158, 11);   // #F59E0B
            public static readonly MediaColor WarningText = MediaColor.FromRgb(252, 211, 77);     // #FCD34D
        }

        public static class LightColors
        {
            public static readonly MediaColor WindowBg = MediaColor.FromRgb(241, 245, 249);       // #F1F5F9
            public static readonly MediaColor CardBg = MediaColor.FromRgb(255, 255, 255);         // #FFFFFF
            public static readonly MediaColor CardBorder = MediaColor.FromRgb(203, 213, 225);     // #CBD5E1
            public static readonly MediaColor InsetBg = MediaColor.FromRgb(248, 250, 252);        // #F8FAFC
            public static readonly MediaColor InsetBorder = MediaColor.FromRgb(203, 213, 225);    // #CBD5E1
            public static readonly MediaColor InputBg = MediaColor.FromRgb(255, 255, 255);        // #FFFFFF
            public static readonly MediaColor InputBorder = MediaColor.FromRgb(148, 163, 184);    // #94A3B8
            public static readonly MediaColor TextPrimary = MediaColor.FromRgb(15, 23, 42);       // #0F172A
            public static readonly MediaColor TextSecondary = MediaColor.FromRgb(51, 65, 85);     // #334155
            public static readonly MediaColor TextMuted = MediaColor.FromRgb(71, 85, 105);        // #475569
            public static readonly MediaColor HeaderBg = MediaColor.FromRgb(226, 232, 240);       // #E2E8F0
            public static readonly MediaColor FooterBg = MediaColor.FromRgb(226, 232, 240);       // #E2E8F0
            public static readonly MediaColor LockBg = MediaColor.FromRgb(254, 226, 226);         // #FEE2E2
            public static readonly MediaColor LockBorder = MediaColor.FromRgb(239, 68, 68);       // #EF4444
            public static readonly MediaColor LockText = MediaColor.FromRgb(153, 27, 27);         // #991B1B
            public static readonly MediaColor SuccessBg = MediaColor.FromRgb(220, 252, 231);      // #DCFCE7
            public static readonly MediaColor SuccessBorder = MediaColor.FromRgb(16, 185, 129);   // #10B981
            public static readonly MediaColor SuccessText = MediaColor.FromRgb(22, 101, 52);      // #166534
            public static readonly MediaColor WarningBg = MediaColor.FromRgb(254, 243, 199);      // #FEF3C7
            public static readonly MediaColor WarningBorder = MediaColor.FromRgb(217, 119, 6);     // #D97706
            public static readonly MediaColor WarningText = MediaColor.FromRgb(146, 64, 14);       // #92400E
        }

        public static void ToggleTheme()
        {
            ApplyTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        }

        public static void ApplyTheme(AppTheme theme)
        {
            CurrentTheme = theme;

            bool isDark = theme == AppTheme.Dark;
            var resources = WpfApp.Current?.Resources;
            if (resources != null)
            {
                resources["ThemeWindowBg"]     = new SolidColorBrush(isDark ? DarkColors.WindowBg : LightColors.WindowBg);
                resources["ThemeCardBg"]       = new SolidColorBrush(isDark ? DarkColors.CardBg : LightColors.CardBg);
                resources["ThemeCardBorder"]   = new SolidColorBrush(isDark ? DarkColors.CardBorder : LightColors.CardBorder);
                resources["ThemeInsetBg"]      = new SolidColorBrush(isDark ? DarkColors.InsetBg : LightColors.InsetBg);
                resources["ThemeInsetBorder"]  = new SolidColorBrush(isDark ? DarkColors.InsetBorder : LightColors.InsetBorder);
                resources["ThemeInputBg"]      = new SolidColorBrush(isDark ? DarkColors.InputBg : LightColors.InputBg);
                resources["ThemeInputBorder"]  = new SolidColorBrush(isDark ? DarkColors.InputBorder : LightColors.InputBorder);
                resources["ThemeTextPrimary"]  = new SolidColorBrush(isDark ? DarkColors.TextPrimary : LightColors.TextPrimary);
                resources["ThemeTextSecondary"]= new SolidColorBrush(isDark ? DarkColors.TextSecondary : LightColors.TextSecondary);
                resources["ThemeTextMuted"]    = new SolidColorBrush(isDark ? DarkColors.TextMuted : LightColors.TextMuted);
                resources["ThemeHeaderBg"]     = new SolidColorBrush(isDark ? DarkColors.HeaderBg : LightColors.HeaderBg);
                resources["ThemeFooterBg"]     = new SolidColorBrush(isDark ? DarkColors.FooterBg : LightColors.FooterBg);
                resources["ThemeLockBg"]       = new SolidColorBrush(isDark ? DarkColors.LockBg : LightColors.LockBg);
                resources["ThemeLockBorder"]   = new SolidColorBrush(isDark ? DarkColors.LockBorder : LightColors.LockBorder);
                resources["ThemeLockText"]     = new SolidColorBrush(isDark ? DarkColors.LockText : LightColors.LockText);
                resources["ThemeSuccessBg"]    = new SolidColorBrush(isDark ? DarkColors.SuccessBg : LightColors.SuccessBg);
                resources["ThemeSuccessBorder"]= new SolidColorBrush(isDark ? DarkColors.SuccessBorder : LightColors.SuccessBorder);
                resources["ThemeSuccessText"]  = new SolidColorBrush(isDark ? DarkColors.SuccessText : LightColors.SuccessText);
                resources["ThemeWarningBg"]    = new SolidColorBrush(isDark ? DarkColors.WarningBg : LightColors.WarningBg);
                resources["ThemeWarningBorder"]= new SolidColorBrush(isDark ? DarkColors.WarningBorder : LightColors.WarningBorder);
                resources["ThemeWarningText"]  = new SolidColorBrush(isDark ? DarkColors.WarningText : LightColors.WarningText);
            }

            ThemeChanged?.Invoke(theme);
        }
    }
}
