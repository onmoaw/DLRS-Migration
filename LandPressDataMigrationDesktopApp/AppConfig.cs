namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Centralized application configuration and version control.
    /// CHANGE THE VERSION NUMBER IN THIS ONE PLACE to automatically
    /// update it everywhere across the application (Window titles, Top App Bar, Footers, etc.).
    /// </summary>
    public static class AppConfig
    {
        // =====================================================================
        // EDIT THIS VERSION NUMBER TO REPLICATE ACROSS THE ENTIRE APPLICATION:
        // =====================================================================
        public const string Version = "v0.8";

        // Derived display strings automatically formatted using the Version constant above:
        public static string AppName => "DLRS Data Migration Tool";

        /// <summary>Format: "DLRS Data Migration Tool  v1.0"</summary>
        public static string AppTitleWithVersion => $"{AppName}  {Version}";

        /// <summary>Format: "DLRS Data Migration Tool (v1.0)"</summary>
        public static string WindowTitle => $"{AppName} ({Version})";

        /// <summary>Format: "DLRS Preparation Wizard  v1.0"</summary>
        public static string DbPrepWizardTitle => $"DLRS Preparation Wizard  {Version}";

        /// <summary>Format: "Data Migration Wizard  v1.0"</summary>
        public static string MigrationWizardTitle => $"Data Migration Wizard  {Version}";
    }
}
