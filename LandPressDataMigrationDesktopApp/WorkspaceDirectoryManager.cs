using System;
using System.IO;
using System.Text.Json;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Central manager for the dedicated database workspace directory.
    /// Ensures that all database files (Core 4103, Intermediate Staging, and Source DBs)
    /// are anchored to a single, persistent folder on disk.
    /// </summary>
    public static class WorkspaceDirectoryManager
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LandPressDataMigration");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "workspace_settings.json");

        private class WorkspaceSettingsModel
        {
            public string? DedicatedFolderPath { get; set; }
        }

        /// <summary>
        /// Loads the previously saved workspace folder path from user configuration, if valid.
        /// </summary>
        public static string? LoadSavedWorkspaceFolder()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var model = JsonSerializer.Deserialize<WorkspaceSettingsModel>(json);
                    if (!string.IsNullOrWhiteSpace(model?.DedicatedFolderPath) && Directory.Exists(model.DedicatedFolderPath))
                    {
                        return NormalizePath(model.DedicatedFolderPath);
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Persists the dedicated workspace folder path to user configuration.
        /// </summary>
        public static void SaveWorkspaceFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                    Directory.CreateDirectory(SettingsDirectory);

                var model = new WorkspaceSettingsModel
                {
                    DedicatedFolderPath = NormalizePath(folderPath)
                };

                string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }

        /// <summary>
        /// Normalizes a file or folder path by resolving full paths and trimming trailing slashes.
        /// </summary>
        public static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd('\\', '/');
            }
        }

        /// <summary>
        /// Checks whether a given database physical file path resides in the dedicated workspace folder.
        /// </summary>
        public static bool IsPathInWorkspace(string? physicalFilePath, string? workspaceFolder)
        {
            if (string.IsNullOrWhiteSpace(physicalFilePath) || string.IsNullOrWhiteSpace(workspaceFolder))
                return false;

            try
            {
                string fileDir = Path.GetDirectoryName(physicalFilePath) ?? string.Empty;
                return string.Equals(NormalizePath(fileDir), NormalizePath(workspaceFolder), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Counts database files (*.mdf, *.bak) present in the workspace folder.
        /// </summary>
        public static (int mdfCount, int bakCount) GetWorkspaceFileCounts(string? workspaceFolder)
        {
            if (string.IsNullOrWhiteSpace(workspaceFolder) || !Directory.Exists(workspaceFolder))
                return (0, 0);

            try
            {
                int mdfs = Directory.GetFiles(workspaceFolder, "*.mdf", SearchOption.TopDirectoryOnly).Length;
                int baks = Directory.GetFiles(workspaceFolder, "*.bak", SearchOption.TopDirectoryOnly).Length;
                return (mdfs, baks);
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>
        /// Ensures the folder exists and creates a temporary test file to verify write access.
        /// </summary>
        public static bool EnsureFolderAndCheckAccess(string folderPath, out string? errorMessage)
        {
            errorMessage = null;
            try
            {
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                string testFile = Path.Combine(folderPath, $".dlrs_access_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "DLRS Workspace Access Test");
                File.Delete(testFile);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
