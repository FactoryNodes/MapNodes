using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using FactoryNodes.MapNodes.Presentation;
using Microsoft.Win32;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace MapNodes;

/// <summary>Un texto fijo del XAML en el idioma del programa: <c>Content="{l:T 'Abrir…'}"</c>. La traducción la busca <see cref="L"/>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}

/// <summary>Pinta un SVG (texto) como imagen WPF, igual que FactoryNodes y SoftNodes. Si no se puede dibujar, null.</summary>
public sealed class SvgTextToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string svg || string.IsNullOrWhiteSpace(svg)) return null;
        try
        {
            var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = false, OptimizePath = true };
            using var reader = new FileSvgReader(settings);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg));
            var drawing = reader.Read(stream);
            if (drawing is null) return null;
            drawing.Freeze();
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Lo que el ViewModel pide a Windows: los diálogos de abrir y guardar, el disco, el portapapeles y el idioma guardado
/// (<c>%APPDATA%\MapNodes\language.txt</c>). La carpeta propuesta para guardar es la de la marca dentro de la última usada.
/// </summary>
public sealed class WpfMapNodesServices : IMapNodesServices
{
    private static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MapNodes");
    private static readonly string LanguageFile = Path.Combine(DataFolder, "language.txt");
    private string? _lastFolder;

    public Window? Owner { get; set; }

    public string? ChooseFileToOpen()
    {
        var dialog = new OpenFileDialog { Filter = MapNodesViewModel.FileFilter + "|*.json", InitialDirectory = _lastFolder ?? "" };
        if (dialog.ShowDialog(Owner) != true) return null;
        _lastFolder = Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }

    public string? ChooseFileToSave(string suggestedName, string suggestedFolder)
    {
        var folder = _lastFolder;
        // Si la última carpeta es la de otra marca, se propone la de esta al lado (library/devices/<marca>)
        if (folder is not null && !Path.GetFileName(folder).Equals(suggestedFolder, StringComparison.OrdinalIgnoreCase) &&
            Path.GetDirectoryName(folder) is { } parent && Directory.Exists(Path.Combine(parent, suggestedFolder)))
            folder = Path.Combine(parent, suggestedFolder);
        var dialog = new SaveFileDialog
        {
            Filter = MapNodesViewModel.FileFilter + "|*.json", FileName = suggestedName, InitialDirectory = folder ?? "", AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(Owner) != true) return null;
        _lastFolder = Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }

    public string ReadText(string path) => File.ReadAllText(path, Encoding.UTF8);

    public void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    public bool Confirm(string message) =>
        MessageBox.Show(Owner!, message, MapNodesViewModel.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return null;   // otro programa tiene el portapapeles abierto
        }
    }

    public string FileName(string path) => Path.GetFileName(path);

    public string? SavedLanguage
    {
        get => File.Exists(LanguageFile) ? File.ReadAllText(LanguageFile).Trim() : null;
        set
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(LanguageFile, value ?? "");
        }
    }

    public CultureInfo SystemCulture { get; } = CultureInfo.CurrentUICulture;

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public string? ChooseDrawingToOpen()
    {
        var dialog = new OpenFileDialog { Filter = "SVG|*.svg", InitialDirectory = _lastFolder ?? "" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public FactoryNodes.Devices.IModbusClient? Connect(string host, int port) => new FactoryNodes.Modbus.ModbusTcpClient(host, port);

    public Func<string, CancellationToken, Task<byte[]>>? HttpBytes => (url, cancellation) => Http.GetByteArrayAsync(url, cancellation);

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add(App.WaitArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            Process.Start(start);
        }

        Application.Current.Shutdown();
    }
}
