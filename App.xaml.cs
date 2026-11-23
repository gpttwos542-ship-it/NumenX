using System.Windows;

namespace NumenX;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Logger.Write("Unhandled UI error: " + e.Exception);
            MessageBox.Show("NumenX hit an unexpected error:\n\n" + e.Exception.Message +
                            "\n\nDetails were saved to the log (Settings > Open log folder).",
                            "NumenX", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
