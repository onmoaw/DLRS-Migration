using System.Configuration;
using System.Data;
using System.Windows;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeManager.ApplyTheme(AppTheme.Light);
        }
    }

}
