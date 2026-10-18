using System;

namespace LandPressDataMigrationDesktopApp
{
    public enum UserRole
    {
        Admin,
        StandardUser
    }

    /// <summary>
    /// Holds the active session state and role privileges for the logged-in user.
    /// </summary>
    public static class UserSession
    {
        public static string CurrentUsername { get; private set; } = "Admin";
        public static UserRole CurrentRole { get; private set; } = UserRole.Admin;

        public static bool IsAdmin => CurrentRole == UserRole.Admin;

        public static void SetUser(string username, UserRole role)
        {
            CurrentUsername = username;
            CurrentRole = role;
        }

        public static void Clear()
        {
            CurrentUsername = "Guest";
            CurrentRole = UserRole.StandardUser;
        }
    }
}
