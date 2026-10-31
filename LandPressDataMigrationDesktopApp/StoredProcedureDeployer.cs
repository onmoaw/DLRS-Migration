using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Helper to verify presence of required Stored Procedures in the target database [DLRSLSINF_4103PRSDB].
    /// Note: Stored procedures are managed directly on the database server.
    /// Source SQL scripts are excluded from application binaries to prevent reverse-engineering.
    /// </summary>
    public static class StoredProcedureDeployer
    {
        public const string SpNameV2 = "SP_LIS_DATA_MIGRATION";
        public const string SpName01 = "SP_LIS_DATA_MIGRATION_01";
        public const string SpNameReport = "SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK";

        /// <summary>
        /// Checks whether the migration stored procedures exist in the target database.
        /// </summary>
        public static async Task<(bool HasV2, bool Has01, bool HasReport)> CheckProceduresAsync(string targetConnectionString)
        {
            try
            {
                using var conn = new SqlConnection(targetConnectionString);
                await conn.OpenAsync();

                const string sql = @"
                    SELECT 
                        CASE WHEN OBJECT_ID('dbo.SP_LIS_DATA_MIGRATION', 'P') IS NOT NULL THEN 1 ELSE 0 END AS HasV2,
                        CASE WHEN OBJECT_ID('dbo.SP_LIS_DATA_MIGRATION_01', 'P') IS NOT NULL THEN 1 ELSE 0 END AS Has01,
                        CASE WHEN OBJECT_ID('dbo.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK', 'P') IS NOT NULL THEN 1 ELSE 0 END AS HasReport;";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    bool v2 = reader.GetInt32(0) == 1;
                    bool v01 = reader.GetInt32(1) == 1;
                    bool rep = reader.GetInt32(2) == 1;
                    return (v2, v01, rep);
                }
            }
            catch
            {
                // Return false on connection failure
            }

            return (false, false, false);
        }

        /// <summary>
        /// Verifies whether all three stored procedures exist in the target database and generates a report.
        /// </summary>
        public static async Task<(bool AllPresent, string Summary)> VerifyProceduresAsync(string targetConnectionString)
        {
            var (hasV2, has01, hasReport) = await CheckProceduresAsync(targetConnectionString);

            var lines = new List<string>
            {
                (hasV2 ? "✔ " : "❌ ") + $"dbo.{SpNameV2}: " + (hasV2 ? "Verified Present" : "Missing in DB"),
                (has01 ? "✔ " : "❌ ") + $"dbo.{SpName01}: " + (has01 ? "Verified Present" : "Missing in DB"),
                (hasReport ? "✔ " : "❌ ") + $"dbo.{SpNameReport}: " + (hasReport ? "Verified Present" : "Missing in DB")
            };

            bool allOk = hasV2 && has01 && hasReport;
            string summary = (allOk ? "All required stored procedures are verified in the target database:\n\n" : "One or more stored procedures are missing in the target database:\n\n")
                           + string.Join("\n", lines);

            if (!allOk)
            {
                summary += "\n\nPlease ensure your database administrator has deployed the required stored procedures on SQL Server.";
            }

            return (allOk, summary);
        }

        /// <summary>
        /// Compatibility alias for pipeline pre-checks.
        /// </summary>
        public static async Task<(bool AllPresent, string Summary)> DeployAllProceduresAsync(string targetConnectionString)
        {
            return await VerifyProceduresAsync(targetConnectionString);
        }

        /// <summary>
        /// Checks if procedures exist in the target database and returns verification status.
        /// </summary>
        public static async Task<(bool AllPresent, string Summary)> EnsureProceduresDeployedAsync(string targetConnectionString)
        {
            return await VerifyProceduresAsync(targetConnectionString);
        }
    }
}
