using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Helper to verify and deploy Stored Procedures from DevResource / Embedded Resources
    /// into the target database [DLRSLSINF_4103PRSDB].
    /// </summary>
    public static class StoredProcedureDeployer
    {
        public const string SpNameV2 = "SP_LIS_DATA_MIGRATION";
        public const string SpName01 = "SP_LIS_DATA_MIGRATION_01";
        public const string SpNameReport = "SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK";

        /// <summary>
        /// Checks whether the 3 migration stored procedures exist in the target database.
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
        /// Reads script content from embedded resources or fallback file system paths.
        /// </summary>
        public static string? LoadScriptContent(string spKey)
        {
            string resourceName = spKey switch
            {
                SpNameV2 => "LandPressDataMigrationDesktopApp.Scripts.SP_LIS_DATA_MIGRATION.sql",
                SpName01 => "LandPressDataMigrationDesktopApp.Scripts.SP_LIS_DATA_MIGRATION_01.sql",
                SpNameReport => "LandPressDataMigrationDesktopApp.Scripts.SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK.sql",
                _ => ""
            };

            // 1. Try reading from embedded resource
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                // Fall back to file system
            }

            // 2. Try file system search
            string[] possiblePaths = spKey switch
            {
                SpNameV2 => new[]
                {
                    @"DevResource\SP_LIS_DATA_MIGRATION v2.txt",
                    @"..\DevResource\SP_LIS_DATA_MIGRATION v2.txt",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"DevResource\SP_LIS_DATA_MIGRATION v2.txt")
                },
                SpName01 => new[]
                {
                    @"DevResource\SP_LIS_DATA_MIGRATION_01 v1.txt",
                    @"..\DevResource\SP_LIS_DATA_MIGRATION_01 v1.txt",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"DevResource\SP_LIS_DATA_MIGRATION_01 v1.txt")
                },
                SpNameReport => new[]
                {
                    @"DevResource\SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK v1.txt",
                    @"..\DevResource\SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK v1.txt",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"DevResource\SP_LIS_DATA_MIGRATION_REPORT_CODEBOOK v1.txt")
                },
                _ => Array.Empty<string>()
            };

            foreach (var p in possiblePaths)
            {
                if (File.Exists(p))
                {
                    return File.ReadAllText(p);
                }
            }

            return null;
        }

        /// <summary>
        /// Deploys or updates the specified stored procedure in the target database.
        /// </summary>
        public static async Task<(bool Success, string Message)> DeployStoredProcedureAsync(string targetConnectionString, string spKey)
        {
            string? script = LoadScriptContent(spKey);
            if (string.IsNullOrWhiteSpace(script))
            {
                return (false, $"SQL script definition for [{spKey}] could not be found in resources or DevResource folder.");
            }

            try
            {
                using var conn = new SqlConnection(targetConnectionString);
                await conn.OpenAsync();

                // 1. Ensure stub exists so ALTER PROCEDURE succeeds
                string stubSql = $"IF OBJECT_ID('dbo.{spKey}', 'P') IS NULL EXEC('CREATE PROCEDURE dbo.{spKey} AS RETURN;');";
                using (var stubCmd = new SqlCommand(stubSql, conn))
                {
                    await stubCmd.ExecuteNonQueryAsync();
                }

                // 2. Split script into batches by 'GO'
                var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

                foreach (var rawBatch in batches)
                {
                    string batch = rawBatch.Trim();
                    if (string.IsNullOrWhiteSpace(batch)) continue;

                    // Skip USE statements as we are already connected to the target DB
                    if (Regex.IsMatch(batch, @"^\s*USE\s+\[?[\w]+\]?\s*$", RegexOptions.IgnoreCase))
                    {
                        continue;
                    }

                    using var cmd = new SqlCommand(batch, conn)
                    {
                        CommandTimeout = 180
                    };
                    await cmd.ExecuteNonQueryAsync();
                }

                return (true, $"Stored Procedure [dbo].[{spKey}] compiled and deployed successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Error deploying [{spKey}]: {ex.Message}");
            }
        }

        /// <summary>
        /// Deploys or updates all three stored procedures in sequence.
        /// </summary>
        public static async Task<(bool AllSucceeded, string Summary)> DeployAllProceduresAsync(string targetConnectionString)
        {
            var results = new System.Collections.Generic.List<string>();
            bool allOk = true;

            string[] procs = { SpNameV2, SpName01, SpNameReport };
            foreach (var p in procs)
            {
                var (ok, msg) = await DeployStoredProcedureAsync(targetConnectionString, p);
                if (!ok) allOk = false;
                results.Add((ok ? "✔ " : "❌ ") + msg);
            }

            return (allOk, string.Join("\n", results));
        }

        /// <summary>
        /// Checks if procedures exist in the target database, and automatically deploys any that are missing.
        /// </summary>
        public static async Task<(bool AllPresent, string Summary)> EnsureProceduresDeployedAsync(string targetConnectionString)
        {
            var (hasV2, has01, hasReport) = await CheckProceduresAsync(targetConnectionString);
            if (!hasV2 || !has01 || !hasReport)
            {
                return await DeployAllProceduresAsync(targetConnectionString);
            }
            return (true, "All stored procedures are present in target database.");
        }
    }
}
