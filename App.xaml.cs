using System.Windows;
using System.Globalization;

namespace DevOpsPulse;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("nb-NO");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
        base.OnStartup(e);
    }
}
