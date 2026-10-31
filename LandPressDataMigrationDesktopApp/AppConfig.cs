using System.Collections.Generic;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Configuration model for individual migration steps in the unified sequential pipeline.
    /// You can reorder, enable/disable, or change parameters HERE in ONE place.
    /// </summary>
    public class MigrationStepConfig
    {
        public int Order { get; set; }
        public string SpName { get; set; } = "dbo.SP_LIS_DATA_MIGRATION";
        public string ProcId { get; set; } = "";
        public string Title { get; set; } = "";
        public string UserTitle { get; set; } = "";
        public string Description { get; set; } = "";
        public string UserDescription { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public bool IsRequired { get; set; } = false;
        public bool RequiresRsNumber { get; set; } = false;
        public bool RequiresJsonInput { get; set; } = false;
        public string DefaultJson { get; set; } = "";
        public bool HasResultTable { get; set; } = false;

        public string GetTitle(bool isAdmin) => (!isAdmin && !string.IsNullOrWhiteSpace(UserTitle)) ? UserTitle : Title;
        public string GetDescription(bool isAdmin) => (!isAdmin && !string.IsNullOrWhiteSpace(UserDescription)) ? UserDescription : Description;
    }

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
        public const string Version = "v0.19a";

        /// <summary>
        /// Permanent intermediate staging database required on the connected SQL Server instance.
        /// </summary>
        public const string IntermediateDbName = "DLRS_Migration_Intrm";

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

        // =====================================================================
        // CENTRALIZED PERFORMANCE & DELAY CONFIGURATION:
        // Set your conversion speed, UI refresh rate, and batch size HERE in ONE place.
        // =====================================================================

        /// <summary>
        /// Delay in milliseconds injected per record during font conversion.
        /// Default: 0 ms (TRUE REAL-TIME maximum speed, ~10,000+ rec/sec).
        /// Set to 1 or 2 ms if you want to intentionally throttle CPU usage.
        /// </summary>
        public static int ConversionRowDelayMs = 2;

        /// <summary>
        /// Real-time progress refresh interval in milliseconds for the conversion progress bar,
        /// record count, speed counter, and ETA calculations.
        /// Default: 50 ms (20 FPS smooth real-time visual progress without lagging the UI thread).
        /// </summary>
        public static int ConversionUiUpdateIntervalMs = 50;

        /// <summary>
        /// Delay in milliseconds between SQL batch update chunks to yield CPU/IO to the server.
        /// Default: 0 ms (immediate dispatch).
        /// </summary>
        public static int BatchUpdateDelayMs = 0;

        /// <summary>
        /// Number of records processed per SQL JSON batch update chunk when committing converted Unicode data to the DB.
        /// Default: 50,000 records per batch.
        /// </summary>
        public static int BatchChunkSize = 50_000;

        // =====================================================================
        // MIGRATION PIPELINE & FAILSAFE CONFIGURATION (CENTRALIZED CONTROL):
        // =====================================================================

        /// <summary>
        /// Whether the final migration wizard executes all steps one by one automatically by default.
        /// Set to true (default) for 1-click automated sequential execution; set to false for manual execution.
        /// </summary>
        public static bool DefaultAutoExecuteAll = true;

        /// <summary>
        /// Failsafe: When true, if any stored procedure or branch was commented out in SQL Server
        /// (raising "Process ID not found to execute" or "Could not find stored procedure"),
        /// the pipeline safely marks the step as bypassed and continues to the next step without crashing or stopping.
        /// </summary>
        public static bool ContinueIfStepCommentedOutInDb = true;

        /// <summary>
        /// Delay in milliseconds between steps in Auto-Run mode to provide a smooth visual transition.
        /// </summary>
        public static int AutoRunStepDelayMs = 300;

        /// <summary>
        /// Failsafe helper: Checks if an exception or message represents a stored procedure or branch
        /// that was commented out or omitted in SQL Server.
        /// </summary>
        public static bool IsCommentedOutOrNotFoundInDb(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            if (!ContinueIfStepCommentedOutInDb) return false;

            return message.Contains("not found to execute", System.StringComparison.OrdinalIgnoreCase)
                || message.Contains("Could not find stored procedure", System.StringComparison.OrdinalIgnoreCase)
                || message.Contains("is not recognized", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// MASTER MIGRATION PIPELINE SEQUENCE:
        /// Control the execution order, enable/disable steps, or change stored procedure bindings HERE in ONE place!
        /// </summary>
        public static List<MigrationStepConfig> GetPipelineSteps() => new()
        {
            // 1. Audit / Pre-Check: Check row counts in intermediate DB
            new MigrationStepConfig
            {
                Order = 1,
                SpName = "dbo.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK",
                ProcId = "GETROWCOUNTS",
                Title = "Pre-Check — Audit Staging Row Counts (GETROWCOUNTS)",
                UserTitle = "Pre-Check — Audit Staging Records",
                Description = "Queries total record counts from tblLinfo01 and tblLinfo02 in staging database before migration.",
                UserDescription = "Audits and verifies the total records staged in the intermediate database before starting migration.",
                IsEnabled = true,
                IsRequired = false,
                HasResultTable = true
            },

            // 2. Prepare Area Information
            new MigrationStepConfig
            {
                Order = 2,
                SpName = "dbo.SP_LIS_DATA_MIGRATION",
                ProcId = "MTO_AREAINF",
                Title = "Step 1 — Prepare Area Information (MTO_AREAINF)",
                UserTitle = "Step 1 — Prepare Area Information",
                Description = "Prepares and imports area and mauza boundaries from the staging database into AREAINF.",
                UserDescription = "Prepares and registers area, mauza, and boundary records into the core database.",
                IsEnabled = true,
                IsRequired = false
            },

            // 3. Verify Khatians & Check Mismatches
            new MigrationStepConfig
            {
                Order = 3,
                SpName = "dbo.SP_LIS_DATA_MIGRATION",
                ProcId = "MANAGE_MISSMATCH",
                Title = "Step 2 — Verify Khatians & Check Mismatches (MANAGE_MISSMATCH)",
                UserTitle = "Step 2 — Verify Khatians & Check Mismatches",
                Description = "Compares tblLinfo01, tblLinfo02, and tblHalsabek to detect mismatching Khatian records.",
                UserDescription = "Compares staged records across tables to detect and audit any mismatching Khatian numbers.",
                IsEnabled = true,
                IsRequired = false,
                HasResultTable = true
            },

            // 4. Import Khatian Master Records (Required)
            new MigrationStepConfig
            {
                Order = 4,
                SpName = "dbo.SP_LIS_DATA_MIGRATION",
                ProcId = "MTO_LINFO00",
                Title = "Step 3 — Import Khatian Master Records (MTO_LINFO00)",
                UserTitle = "Step 3 — Import Khatian Master Records",
                Description = "Migrates Khatian master records into dbo_lis.linfo00 and marks convsts = 'SUCCESS'.",
                UserDescription = "Migrates and registers all core Khatian master land records into the central database.",
                IsEnabled = true,
                IsRequired = true,
                RequiresRsNumber = true
            },

            // 5. Import Plot Details (Required)
            new MigrationStepConfig
            {
                Order = 5,
                SpName = "dbo.SP_LIS_DATA_MIGRATION",
                ProcId = "MTO_LINFO01",
                Title = "Step 4 — Import Plot Details (MTO_LINFO01)",
                UserTitle = "Step 4 — Import Plot Details",
                Description = "Aggregates Halsabek plots, merges areas from tblLinfo01, and inserts into dbo_lis.linfo01.",
                UserDescription = "Aggregates and migrates plot numbers, land areas, and boundaries into the central database.",
                IsEnabled = true,
                IsRequired = true,
                RequiresRsNumber = true
            },

            // 6. Import Land Ownership & Demographics (Required)
            new MigrationStepConfig
            {
                Order = 6,
                SpName = "dbo.SP_LIS_DATA_MIGRATION",
                ProcId = "MTO_LINFO02",
                Title = "Step 5 — Import Land Ownership & Relations (MTO_LINFO02)",
                UserTitle = "Step 5 — Import Land Ownership & Relations",
                Description = "Extracts owner relations, gender, father/spouse name from address fields, and inserts into dbo_lis.linfo02.",
                UserDescription = "Extracts and migrates land ownership, owner shares, and family relations into the central database.",
                IsEnabled = true,
                IsRequired = true,
                RequiresRsNumber = true
            },

            // 7. Update Map Unit Codes
            new MigrationStepConfig
            {
                Order = 7,
                SpName = "dbo.SP_LIS_DATA_MIGRATION",
                ProcId = "UPDTMAPINF01",
                Title = "Step 6 — Update Map Unit Codes (UPDTMAPINF01)",
                UserTitle = "Step 6 — Update Map Unit Codes",
                Description = "Inserts distinct unit codes from tblHalsabek into dbo_lis.mapinf01 for sheet '000'.",
                UserDescription = "Updates and registers map unit boundaries and sheet identifiers in the central database.",
                IsEnabled = true,
                IsRequired = false
            },

            // 8. Import Detailed Plot Attributes
            new MigrationStepConfig
            {
                Order = 8,
                SpName = "dbo.SP_LIS_DATA_MIGRATION",
                ProcId = "MTO_PLOTINF01",
                Title = "Step 7 — Import Detailed Plot Attributes (MTO_PLOTINF01)",
                UserTitle = "Step 7 — Import Detailed Plot Attributes",
                Description = "Migrates distinct plot records into dbo_lis.plotinf01.",
                UserDescription = "Migrates detailed land classification, plot attributes, and spatial records into the central database.",
                IsEnabled = true,
                IsRequired = false,
                RequiresRsNumber = true
            },

            // 9. Additional SP_LIS_DATA_MIGRATION_01 staging step (Can be enabled if needed)
            new MigrationStepConfig
            {
                Order = 9,
                SpName = "dbo.SP_LIS_DATA_MIGRATION_01",
                ProcId = "MTO_LINFUSR",
                Title = "Step 8 — Land Info User Staging (MTO_LINFUSR)",
                UserTitle = "Step 8 — Land Info User Staging",
                Description = "Staging step for land info user credentials (from SP_LIS_DATA_MIGRATION_01).",
                UserDescription = "Prepares user credentials and authorization records.",
                IsEnabled = false, // Disabled by default, change to true to include in sequence
                IsRequired = false
            },

            // 10. Additional SP_LIS_DATA_MIGRATION_01 staging step (Can be enabled if needed)
            new MigrationStepConfig
            {
                Order = 10,
                SpName = "dbo.SP_LIS_DATA_MIGRATION_01",
                ProcId = "MTO_LRSINF01",
                Title = "Step 9 — Land RS Survey Header (MTO_LRSINF01)",
                UserTitle = "Step 9 — Land RS Survey Header",
                Description = "RS survey staging header (from SP_LIS_DATA_MIGRATION_01).",
                UserDescription = "Prepares survey session and settlement header records.",
                IsEnabled = false, // Disabled by default
                IsRequired = false
            },

            // 11. Additional SP_LIS_DATA_MIGRATION_01 staging step (Can be enabled if needed)
            new MigrationStepConfig
            {
                Order = 11,
                SpName = "dbo.SP_LIS_DATA_MIGRATION_01",
                ProcId = "MTO_LRSINF02",
                Title = "Step 10 — Land RS Survey Details (MTO_LRSINF02)",
                UserTitle = "Step 10 — Land RS Survey Details",
                Description = "RS survey staging details (from SP_LIS_DATA_MIGRATION_01).",
                UserDescription = "Prepares detailed survey settlement records.",
                IsEnabled = false, // Disabled by default
                IsRequired = false
            }
        };
    }
}
