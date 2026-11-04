using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using FactoryNodes.Localization;

namespace MapNodes;

/// <summary>
/// La aplicación: pone el idioma guardado (o el de Windows) antes de abrir la ventana y abre el fichero que se le pase (<c>MapNodes.exe
/// MX2.json</c>, o arrastrado sobre el exe). Un error no controlado se enseña y se sigue: no se pierde lo que no está guardado.
/// </summary>
public partial class App : Application
{
    /// <summary>El programa que vuelve a abrirse (cambio de idioma) espera a que el anterior termine.</summary>
    public const string WaitArgument = "--esperar";

    protected override void OnStartup(StartupEventArgs e)
    {
        WaitForPrevious(e.Args);
        var services = new WpfMapNodesServices();
        var language = AppLanguages.Resolve(services.SavedLanguage, services.SystemCulture);
        System.Globalization.CultureInfo.CurrentUICulture = language.Culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = language.Culture;
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);

        var window = new MainWindow(services, e.Args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
        MainWindow = window;
        window.Show();
    }

    private static void WaitForPrevious(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals(WaitArgument, StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var pid)) return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(15000);
        }
        catch (ArgumentException)
        {
            // ya había terminado
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, FactoryNodes.MapNodes.Presentation.MapNodesViewModel.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
