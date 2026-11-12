using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FactoryNodes.Localization;

namespace FactoryNodes.MapNodes.Presentation;

/// <summary>
/// Los textos de MapNodes en el idioma elegido (PENDIENTES 82): <c>L.T("Abrir…")</c>. La clave es el texto en castellano; las traducciones, en
/// <c>Localization/Strings.&lt;idioma&gt;.resx</c>. Los usan los ViewModels y los XAML (<c>{l:T 'Texto'}</c>).
/// </summary>
public static class L
{
    private static readonly Texts Strings = Texts.For<MapNodesViewModel>("FactoryNodes.MapNodes.Presentation.Localization.Strings");

    public static string T(string spanish) => Strings.T(spanish);

    public static string F(string spanishFormat, params object?[] args) => Strings.F(spanishFormat, args);
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null) : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Una opción de un desplegable: el valor y su texto.</summary>
public sealed record Option<T>(T Value, string Text);

/// <summary>Un idioma en la lista del botón del globo: su nombre en su idioma («Català»), si es el de ahora y elegirlo.</summary>
public sealed class LanguageChoice(AppLanguage language, bool isCurrent, Action<AppLanguage> choose)
{
    public AppLanguage Language { get; } = language;
    public string Name => Language.NativeName;
    public bool IsCurrent { get; } = isCurrent;
    public ICommand ChooseCommand { get; } = new RelayCommand(() => choose(language));
}

/// <summary>
/// Dibujos (SVG 24×24) de los botones de MapNodes (docs/STYLE.md «Botones con dibujo»): cada uno es un <c>Icons/&lt;nombre&gt;.svg</c> de esta
/// librería, incrustado en la DLL, para que el exe y un web usen los mismos. El color solo con significado: verde = crear, rojo = quitar.
/// </summary>
public static class MapNodesIcons
{
    public static string New => Get("file-new");
    public static string Open => Get("file-open");
    public static string Save => Get("file-save");
    public static string SaveAs => Get("file-save-as");
    public static string Paste => Get("table-paste");
    public static string AddRow => Get("row-add");
    public static string RemoveRow => Get("row-remove");
    public static string Language => Get("language");
    public static string WebOpen => Get("web-open");
    public static string Share => Get("share");
    public static string DeviceTest => Get("device-test");
    public static string DrawingOpen => Get("drawing-open");

    private const string Folder = "Icons/";
    private static readonly Assembly Assembly = typeof(MapNodesIcons).Assembly;
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>Nombres (sin extensión) de todos los iconos incrustados.</summary>
    public static IReadOnlyList<string> Names { get; } = Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Folder, StringComparison.Ordinal) && n.EndsWith(".svg", StringComparison.Ordinal))
        .Select(n => n[Folder.Length..^".svg".Length]).Order(StringComparer.Ordinal).ToList();

    public static string Get(string name) => Cache.GetOrAdd(name, n =>
    {
        using var stream = Assembly.GetManifestResourceStream(Folder + n + ".svg") ?? throw new InvalidOperationException($"No hay icono «{n}».");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}

/// <summary>
/// Lo que el ViewModel pide a la app (cada una lo hace a su manera: el exe con diálogos de Windows y el disco; un web, con subir y bajar ficheros).
/// Así el ViewModel no toca ni disco ni ventanas.
/// </summary>
public interface IMapNodesServices
{
    /// <summary>Elegir un fichero para abrir (null = cancelado). Devuelve su ruta.</summary>
    string? ChooseFileToOpen();

    /// <summary>Elegir dónde guardar, con un nombre propuesto (<c>MX2.json</c>) y la carpeta propuesta (<c>omron</c>); null = cancelado.</summary>
    string? ChooseFileToSave(string suggestedName, string suggestedFolder);

    string ReadText(string path);

    void WriteText(string path, string text);

    /// <summary>Una pregunta de sí o no.</summary>
    bool Confirm(string message);

    /// <summary>El texto del portapapeles (de fuera: se valida al pegar).</summary>
    string? ClipboardText();

    /// <summary>El nombre del fichero sin la carpeta (para el título).</summary>
    string FileName(string path);

    /// <summary>El idioma de la interfaz que se guardó (código: «ca», «en»…; null = el de Windows).</summary>
    string? SavedLanguage { get; set; }

    /// <summary>El de Windows (para elegir uno de los seis al empezar).</summary>
    System.Globalization.CultureInfo SystemCulture { get; }

    /// <summary>Cerrar y volver a abrir el programa (cambio de idioma).</summary>
    void Restart();

    // Lo de abajo, desde el 2026-11-12 (terminar el punto 200); con su valor de siempre para quien no lo tenga (las pruebas, un web)

    /// <summary>Elegir un dibujo SVG para el equipo (null = cancelado). Devuelve su ruta.</summary>
    string? ChooseDrawingToOpen() => null;

    /// <summary>Un cliente Modbus TCP con ese equipo, para «Probar con el equipo» (null = este programa no se conecta).</summary>
    FactoryNodes.Devices.IModbusClient? Connect(string host, int port) => null;

    /// <summary>Los bytes de una dirección de la web (el catálogo publicado); null = este programa no se conecta.</summary>
    Func<string, CancellationToken, Task<byte[]>>? HttpBytes => null;

    /// <summary>Abre una dirección en el navegador.</summary>
    void OpenUrl(string url) { }
}
