using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using FactoryNodes.Devices;

namespace FactoryNodes.MapNodes.Presentation;

/// <summary>
/// Lo que se añadió para terminar el punto 200 (autor, 2026-11-12: «terminamos el punto 200, que mapnodes se actualice también»): el tipo de la
/// lista cerrada, la versión y el autor del fichero; el dibujo; «Probar con el equipo»; abrir un modelo de la web y compartir el propio.
/// </summary>
public sealed partial class MapNodesViewModel
{
    /// <summary>Dónde se comparte un fichero (el portal del cliente, con su cuenta: se sabe de quién es cada uno).</summary>
    public const string ShareUrl = "https://cuenta.factorynodes.com/cuenta/dispositivos";

    private DeviceTypes _types = DeviceTypes.BuiltIn;
    private string _testHost = "", _testPort = "502", _testUnit = "1", _testText = "";
    private bool _testing;

    private void InitMore()
    {
        LoadDrawingCommand = new RelayCommand(LoadDrawing);
        RemoveDrawingCommand = new RelayCommand(RemoveDrawing, () => HasDrawing);
        TestCommand = new RelayCommand(() => _ = TestAsync(), () => !_testing);
        OpenWebCommand = new RelayCommand(() =>
        {
            if (IsDirty && !_services.Confirm(OpenQuestion)) return;
            // Antes de abrir: la ventana es modal y, hasta que se cierra, no se llega a la línea siguiente (2026-11-12: salía vacía)
            _ = Web.LoadAsync();
            Web.IsOpen = true;
        });
        ShareCommand = new RelayCommand(Share);
    }

    // ---------- Tipo, versión y autor ----------

    /// <summary>Los tipos de la lista cerrada y, al final, «Otros» (sin tipo).</summary>
    public IReadOnlyList<Option<string>> TypeOptions =>
    [
        .. _types.All.Select(t => new Option<string>(t.Key, t.Text(CurrentLanguage.Code))),
        new(DeviceTypes.Other, L.T("Otros"))
    ];

    public string Type
    {
        get => _types.KeyOf(_model.Type);
        set
        {
            if (value is null || value == _model.Type) return;
            _model.Type = value;
            Changed(nameof(Type));
        }
    }

    internal DeviceTypes Types => _types;

    /// <summary>Un índice más nuevo trae tipos que este programa no conocía.</summary>
    internal void AddTypes(IEnumerable<DeviceType> types)
    {
        _types = _types.With(types);
        OnPropertyChanged(nameof(TypeOptions));
        OnPropertyChanged(nameof(Type));
    }

    /// <summary>La versión del fichero (1, 2, 3…): se sube con cada corrección que se publica.</summary>
    public int Version
    {
        get => _model.Version;
        set
        {
            if (value == _model.Version) return;
            _model.Version = value;
            Changed(nameof(Version));
        }
    }

    public string Author { get => _model.Author; set => Set(value, v => _model.Author = v, _model.Author); }

    // ---------- El dibujo ----------

    public ICommand LoadDrawingCommand { get; private set; } = null!;
    public ICommand RemoveDrawingCommand { get; private set; } = null!;

    /// <summary>El dibujo del equipo (SVG), para verlo.</summary>
    public string Svg => _model.Svg;

    private void LoadDrawing()
    {
        if (_services.ChooseDrawingToOpen() is not { } path) return;
        string text;
        try
        {
            text = _services.ReadText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = L.F("No se pudo leer {0}: {1}", _services.FileName(path), e.Message);
            return;
        }

        var start = text.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || !text.Contains("</svg>", StringComparison.OrdinalIgnoreCase))
        {
            Status = L.F("{0} no es un dibujo SVG.", _services.FileName(path));
            return;
        }

        // sin la cabecera de XML ni lo de delante: el fichero lleva el <svg> entero
        _model.Svg = text[start..].Trim();
        DrawingChanged();
        Status = L.F("Dibujo cargado de {0}.", _services.FileName(path));
    }

    private void RemoveDrawing()
    {
        _model.Svg = "";
        DrawingChanged();
        Status = L.T("Dibujo quitado.");
    }

    private void DrawingChanged()
    {
        OnPropertyChanged(nameof(Svg));
        Changed(nameof(HasDrawing));
        ((RelayCommand)RemoveDrawingCommand).RaiseCanExecuteChanged();
    }

    // ---------- Probar con el equipo ----------

    public ICommand TestCommand { get; private set; } = null!;

    /// <summary>La IP del equipo (o de su pasarela Modbus TCP/RTU).</summary>
    public string TestHost { get => _testHost; set { _testHost = value?.Trim() ?? ""; OnPropertyChanged(); } }

    public string TestPort { get => _testPort; set { _testPort = value?.Trim() ?? ""; OnPropertyChanged(); } }

    /// <summary>El número de esclavo.</summary>
    public string TestUnit { get => _testUnit; set { _testUnit = value?.Trim() ?? ""; OnPropertyChanged(); } }

    /// <summary>Lo que ha dado la prueba: cuántos se leen y cuántos no.</summary>
    public string TestText
    {
        get => _testText;
        private set
        {
            _testText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Lee del equipo todos los parámetros con dirección Modbus y pone en cada fila lo leído (con su escala y sus decimales) o por qué no: para
    /// ver que las direcciones, los tipos y el orden de las palabras cuadran con lo que enseña el equipo. Solo lee.
    /// </summary>
    public async Task TestAsync()
    {
        if (_testing) return;
        Flush();
        if (TestHost.Length == 0 || !int.TryParse(TestPort, out var port) || port is < 1 or > 65535 || !byte.TryParse(TestUnit, out var unit))
        {
            TestText = L.T("Pon la IP del equipo, el puerto (502) y el número de esclavo (0 a 255).");
            return;
        }

        var (model, problems) = DeviceModel.Parse(_model.ToJson());
        if (model is null)
        {
            TestText = L.F("Antes hay que arreglar los problemas del fichero: {0}", string.Join(" ", problems));
            return;
        }

        if (_services.Connect(TestHost, port) is not { } client)
        {
            TestText = L.T("Este programa no puede conectarse a un equipo.");
            return;
        }

        _testing = true;
        ((RelayCommand)TestCommand).RaiseCanExecuteChanged();
        TestText = L.F("Leyendo de {0}…", TestHost);
        foreach (var row in Parameters) row.TestValue = "";
        try
        {
            var wanted = model.Parameters.Where(p => p.Address.Length > 0).ToList();
            var readings = await DeviceReader.ReadAsync(client, unit, model, wanted);
            var ok = 0;
            foreach (var reading in readings)
            {
                var row = Parameters.FirstOrDefault(r => r.Code.Equals(reading.Parameter.Code, StringComparison.OrdinalIgnoreCase));
                if (row is null) continue;
                if (reading.Ok) ok++;
                row.TestValue = reading.Ok ? DeviceValues.Text(DeviceValues.Resolve(reading.Parameter, readings), reading.Raw!.Value) : "— " + reading.Problem;
            }

            TestText = ok == readings.Count ? L.F("Leídos los {0} parámetros.", ok)
                : L.F("Leídos {0} de {1} parámetros: los demás dicen por qué en su fila.", ok, readings.Count);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            TestText = L.F("No se pudo leer de {0}: {1}", TestHost, e.Message);
        }
        finally
        {
            if (client is IAsyncDisposable disposable) await disposable.DisposeAsync();
            _testing = false;
            ((RelayCommand)TestCommand).RaiseCanExecuteChanged();
        }
    }

    // ---------- La web: abrir uno publicado y compartir el propio ----------

    public ICommand OpenWebCommand { get; private set; } = null!;
    public ICommand ShareCommand { get; private set; } = null!;

    /// <summary>La lista de los publicados (se enseña encima de la ventana mientras <see cref="MapNodesWebViewModel.IsOpen"/>).</summary>
    public MapNodesWebViewModel Web { get; private set; } = null!;

    /// <summary>Abre un fichero bajado de la web (ya comprobado con el índice firmado): sin ruta, como uno nuevo.</summary>
    internal void OpenDownloaded(string json, string key)
    {
        var (model, _) = DeviceModel.Read(json);
        if (model is null) return;
        Load(model, null);
        Status = L.F("Abierto {0} de la web: {1} parámetros. Guárdalo donde quieras.", key, model.Parameters.Count);
    }

    /// <summary>
    /// Compartir: el fichero se guarda y se abre el portal, donde se sube con la cuenta de la empresa (autor, 2026-11-12: subir, solo con cuenta).
    /// Entra en la cola de revisión; publicado, lleva la licencia CC-BY 4.0 y el nombre de quien lo hizo.
    /// </summary>
    private void Share()
    {
        if (HasProblems)
        {
            Status = L.T("Antes de compartirlo hay que arreglar los problemas del fichero (abajo).");
            return;
        }

        if (_path is null || IsDirty) Save(false);
        if (_path is null || IsDirty) return;
        _services.OpenUrl(ShareUrl);
        Status = L.F("Guardado en {0}. En la página que se ha abierto, entra con tu cuenta y sube ese fichero: lo revisamos antes de publicarlo (licencia CC-BY 4.0).", _path);
    }
}

public sealed partial class ParameterRow
{
    private string _testValue = "";

    /// <summary>Lo leído del equipo con «Probar con el equipo» (o por qué no se pudo); no se guarda.</summary>
    public string TestValue
    {
        get => _testValue;
        internal set
        {
            _testValue = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Los decimales que da otro parámetro (un peso), en una línea: el código del que los da y, si hace falta, el desplazamiento, la máscara y
    /// la tabla. «StabDec >>8 &amp;255» (el byte alto), «DspDiv: 0=3; 5=2» (según el valor). Vacío = solo la escala.
    /// </summary>
    public string DecimalsFrom
    {
        get => DecimalsText(Parameter.DecimalsFrom);
        set
        {
            var parsed = DecimalsOf(value ?? "");
            if (DecimalsText(parsed) == DecimalsFrom)
            {
                OnPropertyChanged();
                return;
            }

            Parameter.DecimalsFrom = parsed;
            Done();
        }
    }

    public static string DecimalsText(DeviceDecimals? decimals)
    {
        if (decimals is null || decimals.From.Trim().Length == 0) return "";
        var text = decimals.From.Trim();
        if (decimals.Shift != 0) text += $" >>{decimals.Shift}";
        if (decimals.Mask != 0) text += $" &{decimals.Mask}";
        if (decimals.Map is { Count: > 0 } map) text += ": " + string.Join("; ", map.Select(kv => $"{kv.Key}={kv.Value}"));
        return text;
    }

    /// <summary>De «Código >>8 &amp;255: 0=3; 5=2» a los decimales; vacío = null. Lo que no se entiende se salta (lo dirá la comprobación).</summary>
    public static DeviceDecimals? DecimalsOf(string text)
    {
        var at = text.IndexOf(':');
        var head = (at < 0 ? text : text[..at]).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (head.Length == 0) return null;
        var decimals = new DeviceDecimals { From = head[0] };
        foreach (var part in head.Skip(1))
        {
            if (part.StartsWith(">>", StringComparison.Ordinal) && int.TryParse(part[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var shift)) decimals.Shift = shift;
            else if (part.StartsWith('&') && Number(part[1..]) is { } mask) decimals.Mask = mask;
        }

        if (at >= 0)
        {
            var map = new Dictionary<string, int>();
            foreach (var pair in text[(at + 1)..].Split(';', ',').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && int.TryParse(pair[(eq + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var places)) map[pair[..eq].Trim()] = places;
            }

            if (map.Count > 0) decimals.Map = map;
        }

        return decimals;
    }

    private static long? Number(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null
            : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec) ? dec : null;
}

/// <summary>
/// «Abrir de la web» (PENDIENTES 200): los ficheros publicados por FactoryNodes, por tipo y marca; el elegido se baja (índice firmado, huella
/// comprobada) y se abre para verlo o para partir de él. Sin cuenta: bajar, cualquiera.
/// </summary>
public sealed class MapNodesWebViewModel : ObservableObject
{
    public const string All = "*";

    private readonly MapNodesViewModel _owner;
    private readonly DeviceDownloads? _web;
    private DeviceIndex? _index;
    private string _status = "", _type = All, _brand = All;
    private bool _open, _busy;
    private MapNodesWebRow? _selected;

    internal MapNodesWebViewModel(MapNodesViewModel owner, DeviceDownloads? web)
    {
        _owner = owner;
        _web = web;
        OpenCommand = new RelayCommand(() => _ = OpenAsync(), () => !_busy && Selected is not null);
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    public ObservableCollection<MapNodesWebRow> Rows { get; } = [];
    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }

    public bool IsOpen { get => _open; set { _open = value; OnPropertyChanged(); } }

    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    public MapNodesWebRow? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            OnPropertyChanged();
            ((RelayCommand)OpenCommand).RaiseCanExecuteChanged();
        }
    }

    public IReadOnlyList<Option<string>> TypeOptions =>
    [
        new(All, L.T("Todos los tipos")),
        .. (_index?.Models ?? []).Select(m => _owner.Types.KeyOf(m.Type)).Distinct(StringComparer.OrdinalIgnoreCase).Select(k => new Option<string>(k, TypeText(k)))
    ];

    public string Type
    {
        get => _type;
        set
        {
            if (value is null || value == _type) return;
            (_type, _brand) = (value, All);
            OnPropertyChanged();
            OnPropertyChanged(nameof(BrandOptions));
            OnPropertyChanged(nameof(Brand));
            Fill();
        }
    }

    public IReadOnlyList<Option<string>> BrandOptions =>
    [
        new(All, L.T("Todas las marcas")),
        .. (_index?.Models ?? []).Where(OfType).Select(m => m.Brand).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Select(b => new Option<string>(b, b))
    ];

    public string Brand
    {
        get => _brand;
        set
        {
            if (value is null || value == _brand) return;
            _brand = value;
            OnPropertyChanged();
            Fill();
        }
    }

    private string TypeText(string key) => _owner.Types.Text(key, _owner.CurrentLanguage.Code, L.T("Otros"));

    private bool OfType(DeviceIndexEntry entry) => _type == All || _owner.Types.KeyOf(entry.Type).Equals(_type, StringComparison.OrdinalIgnoreCase);

    public async Task LoadAsync()
    {
        if (_web is null)
        {
            Status = L.T("Este programa no puede conectarse a la web.");
            return;
        }

        Busy(true);
        Status = L.T("Leyendo la lista de la web…");
        try
        {
            _index = await _web.IndexAsync();
            _owner.AddTypes(_index.Types);
            OnPropertyChanged(nameof(TypeOptions));
            OnPropertyChanged(nameof(BrandOptions));
            Fill();
            Status = L.F("{0} modelos publicados. Elige uno y pulsa «Abrir».", _index.Models.Count);
        }
        catch (DeviceDownloadException e)
        {
            Status = Problem(e);
        }
        finally
        {
            Busy(false);
        }
    }

    private void Fill()
    {
        Rows.Clear();
        foreach (var entry in (_index?.Models ?? []).Where(m => OfType(m) && (_brand == All || m.Brand.Equals(_brand, StringComparison.OrdinalIgnoreCase))))
            Rows.Add(new MapNodesWebRow(entry, TypeText(entry.Type)));
        Selected = Rows.FirstOrDefault();
    }

    /// <summary>Baja el elegido y lo abre en el editor.</summary>
    public async Task OpenAsync()
    {
        if (_web is null || Selected is not { } row) return;
        Busy(true);
        Status = L.F("Bajando {0}…", row.Entry.Key);
        try
        {
            var files = await _web.PackageAsync(row.Entry);
            _owner.OpenDownloaded(files[0].Json, row.Entry.Key);
            IsOpen = false;
        }
        catch (DeviceDownloadException e)
        {
            Status = Problem(e);
        }
        finally
        {
            Busy(false);
        }
    }

    private static string Problem(DeviceDownloadException e) => e.Reason switch
    {
        DeviceDownloadFailure.Unreachable => L.F("No se llega a la web de FactoryNodes: {0}", e.Detail),
        DeviceDownloadFailure.BadIndex => L.T("La lista de la web no lleva la firma de FactoryNodes: no se usa."),
        _ => L.F("El fichero de {0} no es el que dice la lista firmada: no se usa.", e.Detail)
    };

    private void Busy(bool busy)
    {
        _busy = busy;
        ((RelayCommand)OpenCommand).RaiseCanExecuteChanged();
    }
}

/// <summary>Un modelo publicado, en la lista de «Abrir de la web».</summary>
public sealed class MapNodesWebRow(DeviceIndexEntry entry, string typeText)
{
    public DeviceIndexEntry Entry { get; } = entry;
    public string TypeText { get; } = typeText;
    public string Brand => Entry.Brand;
    public string Model => Entry.Model;
    public string Kind => Entry.Kind;
    public string Description => Entry.Description;
    public string VersionText => $"v{Entry.Version}";

    /// <summary>Los idiomas en que está: el del fichero principal y los de sus traducciones.</summary>
    public string Languages => string.Join(", ", new[] { Entry.Language }.Concat(Entry.Translations.Select(t => t.Language)).Where(l => l.Length > 0));

    public string VerifiedText => Entry.Verified ? L.T("Probado en un equipo") : L.T("Sin probar en un equipo");
    public string Author => Entry.Author.Length > 0 ? Entry.Author : "FactoryNodes";
}
