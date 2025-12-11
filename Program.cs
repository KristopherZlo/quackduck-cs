using System.Windows.Forms;

namespace QuackDuck;

internal static class Program
{
    // Entry point for the application, prepares WinForms and opens the pet window.
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load();
        Application.Run(new PetForm(settings, scale: 3));
    }
}
