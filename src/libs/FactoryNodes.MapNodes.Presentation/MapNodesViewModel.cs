using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using FactoryNodes.Devices;
using FactoryNodes.Localization;

namespace FactoryNodes.MapNodes.Presentation;

/// <summary>
/// La ventana de MapNodes (PENDIENTES 200; autor, 2026-11-03: «si yo encuentro un pdf y no quiero dártelo para que lo conviertas, poder escribir la
/// cabecera, protocolo, parámetros… a mano, por quien quiera hacerlo»): un fichero de dispositivo (<c>library/devices/&lt;marca&gt;/&lt;modelo&gt;.json</c>,
/// formato de <c>FactoryNodes.Devices/FORMAT.md</c>) abierto para editar. Cabecera y protocolo a la izquierda; parámetros, grupos y órdenes en
/// pestañas; abajo, lo que no vale (lo mismo que comprueba el programa al leerlo). Lo que la ventana no enseña (el dibujo, los decimales que da
/// otro parámetro) se conserva tal cual al guardar.
/// </summary>
public sealed class MapNodesViewModel : ObservableObject
{
    public const string ProductName = "MapNodes";

    /// <summary>Los textos que pinta la app por su cuenta (diálogos de Windows): aquí, para que se traduzcan con los demás.</summary>
    public static string FileFilter => L.T("Ficheros de dispositivo (*.json)");

    public static string CloseQuestion => L.T("Hay cambios sin guardar. ¿Cerrar igualmente?");

    public static string OpenQuestion => L.T("Hay cambios sin guardar. ¿Abrir otro igualmente?");

    private readonly IMapNodesServices _services;
    private DeviceModel _model = NewModel();
    private string? _path;
    private bool _dirty;
    private string _status = "";

    public MapNodesViewModel(IMapNodesServices services)
    {
        _services = services;
        NewCommand = new RelayCommand(New);
        OpenCommand = new RelayCommand(Open);
        SaveCommand = new RelayCommand(() => Save(false));
        SaveAsCommand = new RelayCommand(() => Save(true));
        PasteParametersCommand = new RelayCommand(() => PasteParameters(_services.ClipboardText() ?? ""));
        AddParameterCommand = new RelayCommand(() => AddParameter());
        RemoveParameterCommand = new RelayCommand(p => { if (p is ParameterRow row) RemoveParameter(row); });
        AddGroupCommand = new RelayCommand(() => AddGroup());
        RemoveGroupCommand = new RelayCommand(p => { if (p is GroupRow row) RemoveGroup(row); });
        AddCommandCommand = new RelayCommand(() => AddCommand());
        RemoveCommandCommand = new RelayCommand(p => { if (p is CommandRow row) RemoveCommand(row); });
        Rebuild();
        Status = L.T("Escribe la cabecera y el protocolo, y añade los parámetros (o pégalos de una hoja de cálculo).");
    }

    // ---------- La ventana ----------

    public string Title => $"{ProductName} · {(_model.Brand.Length + _model.Model.Length > 0 ? _model.Key.Trim() : L.T("(sin nombre)"))}" +
                           (_path is null ? "" : $" · {_services.FileName(_path)}") + (_dirty ? " •" : "");

    public bool IsDirty
    {
        get => _dirty;
        private set
        {
            if (_dirty == value) return;
            _dirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>La línea de abajo: qué se acaba de hacer.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand PasteParametersCommand { get; }
    public ICommand AddParameterCommand { get; }
    public ICommand RemoveParameterCommand { get; }
    public ICommand AddGroupCommand { get; }
    public ICommand RemoveGroupCommand { get; }
    public ICommand AddCommandCommand { get; }
    public ICommand RemoveCommandCommand { get; }

    /// <summary>El modelo tal como se guardaría (para quien lo use sin pasar por el fichero, y para las pruebas).</summary>
    public DeviceModel Model
    {
        get
        {
            Flush();
            return _model;
        }
    }

    // ---------- Idioma ----------

    public IReadOnlyList<LanguageChoice> Languages =>
        [.. AppLanguages.All.Select(l => new LanguageChoice(l, l.Code == CurrentLanguage.Code, ChooseLanguage))];

    public AppLanguage CurrentLanguage => AppLanguages.Resolve(_services.SavedLanguage, _services.SystemCulture);

    private void ChooseLanguage(AppLanguage language)
    {
        if (language.Code == CurrentLanguage.Code) return;
        if (IsDirty && !_services.Confirm(L.T("Hay cambios sin guardar. ¿Cambiar de idioma igualmente? El programa se vuelve a abrir y se pierden."))) return;
        _services.SavedLanguage = language.Code;
        IsDirty = false;   // ya se ha preguntado: al cerrar no se vuelve a preguntar
        _services.Restart();
    }

    // ---------- Cabecera ----------

    public string Brand { get => _model.Brand; set => Set(value, v => _model.Brand = v, _model.Brand); }
    public string ModelName { get => _model.Model; set => Set(value, v => _model.Model = v, _model.Model); }
    public string Kind { get => _model.Kind; set => Set(value, v => _model.Kind = v, _model.Kind); }
    public string Description { get => _model.Description; set => Set(value, v => _model.Description = v, _model.Description); }
    public string Source { get => _model.Source; set => Set(value, v => _model.Source = v, _model.Source); }

    public bool Verified
    {
        get => _model.Verified;
        set
        {
            if (value == _model.Verified) return;
            _model.Verified = value;
            Changed(nameof(Verified));
        }
    }

    /// <summary>Lleva dibujo (se conserva; MapNodes no lo edita).</summary>
    public bool HasDrawing => _model.Svg.Trim().Length > 0;

    // ---------- Protocolo ----------

    public IReadOnlyList<Option<int>> RegisterBases { get; } =
        [new(0, L.T("0: la dirección del manual ya es la de la trama")), new(1, L.T("1: el manual numera desde 1 (se resta 1)"))];

    public int RegisterBase
    {
        get => _model.Protocol.RegisterBase;
        set
        {
            if (value == _model.Protocol.RegisterBase) return;
            _model.Protocol.RegisterBase = value;
            Changed(nameof(RegisterBase));
        }
    }

    public IReadOnlyList<Option<DeviceWordOrder>> WordOrders { get; } =
        [new(DeviceWordOrder.HighFirst, L.T("Palabra alta primero")), new(DeviceWordOrder.LowFirst, L.T("Palabra baja primero"))];

    public DeviceWordOrder WordOrder
    {
        get => _model.Protocol.WordOrder;
        set
        {
            if (value == _model.Protocol.WordOrder) return;
            _model.Protocol.WordOrder = value;
            Changed(nameof(WordOrder));
        }
    }

    public int MaxRegistersPerRead
    {
        get => _model.Protocol.MaxRegistersPerRead;
        set
        {
            if (value == _model.Protocol.MaxRegistersPerRead) return;
            _model.Protocol.MaxRegistersPerRead = value;
            Changed(nameof(MaxRegistersPerRead));
        }
    }

    private string _saveAddress = "", _saveDescription = "";
    private int _saveValue;

    /// <summary>La orden de guardar en EEPROM tras escribir: dirección (vacía = el equipo guarda solo), valor y texto.</summary>
    public string SaveAddress { get => _saveAddress; set => SetSave(ref _saveAddress, value.Trim()); }

    public int SaveValue { get => _saveValue; set => SetSave(ref _saveValue, value); }

    public string SaveDescription { get => _saveDescription; set => SetSave(ref _saveDescription, value); }

    public string RestoreNotes { get => _model.Protocol.RestoreNotes; set => Set(value, v => _model.Protocol.RestoreNotes = v, _model.Protocol.RestoreNotes); }

    private void SetSave<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed(name!);
    }

    // ---------- Tablas ----------

    public ObservableCollection<ParameterRow> Parameters { get; } = [];
    public ObservableCollection<GroupRow> Groups { get; } = [];
    public ObservableCollection<CommandRow> Commands { get; } = [];

    /// <summary>Los grupos para el desplegable de cada parámetro (con «0: sin grupo»).</summary>
    public IReadOnlyList<Option<int>> GroupOptions =>
        [new(0, L.T("0 · sin grupo")), .. Groups.Where(g => g.Number > 0).Select(g => new Option<int>(g.Number, $"{g.Number} · {g.Text}"))];

    public string ParametersHeader => L.F("Parámetros ({0})", Parameters.Count);
    public string GroupsHeader => L.F("Grupos ({0})", Groups.Count);
    public string CommandsHeader => L.F("Órdenes ({0})", Commands.Count);

    public ParameterRow AddParameter()
    {
        var parameter = new DeviceParameter { Code = UniqueCode() };
        _model.Parameters.Add(parameter);
        var row = new ParameterRow(parameter, this);
        Parameters.Add(row);
        Changed(nameof(ParametersHeader));
        Status = L.F("Parámetro {0} añadido: escribe su código, su dirección y su tipo.", parameter.Code);
        return row;
    }

    public void RemoveParameter(ParameterRow row)
    {
        _model.Parameters.Remove(row.Parameter);
        Parameters.Remove(row);
        Changed(nameof(ParametersHeader));
        Status = L.F("Parámetro {0} quitado.", row.Code);
    }

    public GroupRow AddGroup()
    {
        var row = new GroupRow(Groups.Select(g => g.Number).DefaultIfEmpty(0).Max() + 1, "", this);
        Groups.Add(row);
        GroupsChanged();
        Status = L.F("Grupo {0} añadido: escribe su texto.", row.Number);
        return row;
    }

    public void RemoveGroup(GroupRow row)
    {
        Groups.Remove(row);
        GroupsChanged();
        Status = L.F("Grupo {0} quitado (sus parámetros quedan con ese número hasta que les cambies el grupo).", row.Number);
    }

    public CommandRow AddCommand()
    {
        var command = new DeviceCommand { Text = L.T("Orden nueva") };
        _model.Commands.Add(command);
        var row = new CommandRow(command, this);
        Commands.Add(row);
        Changed(nameof(CommandsHeader));
        Status = L.T("Orden añadida: escribe su texto, la dirección y el valor que se escribe.");
        return row;
    }

    public void RemoveCommand(CommandRow row)
    {
        _model.Commands.Remove(row.Command);
        Commands.Remove(row);
        Changed(nameof(CommandsHeader));
        Status = L.F("Orden «{0}» quitada.", row.Text);
    }

    internal void GroupsChanged()
    {
        OnPropertyChanged(nameof(GroupOptions));
        foreach (var row in Parameters) row.RefreshGroups();
        Changed(nameof(GroupsHeader));
    }

    private string UniqueCode()
    {
        var used = new HashSet<string>(_model.Parameters.Select(p => p.Code), StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
            if (!used.Contains($"P{n:000}")) return $"P{n:000}";
    }

    // ---------- Pegar de una hoja de cálculo ----------

    /// <summary>
    /// Añade los parámetros copiados de una hoja de cálculo (o escritos a mano), uno por línea, en columnas separadas por tabuladores o por punto y
    /// coma: código, dirección, tipo, escala, unidad, descripción, grupo, clase (ajuste o en vivo) y si se escribe. Solo el código y la dirección
    /// hacen falta; una primera fila de títulos («Código», «Code») se salta; un código que ya está se actualiza en lugar de repetirse. Devuelve
    /// cuántos se añadieron o cambiaron.
    /// </summary>
    public int PasteParameters(string text)
    {
        var added = 0;
        var updated = 0;
        var skipped = 0;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0))
        {
            var cells = (line.Contains('\t') ? line.Split('\t') : line.Split(';')).Select(c => c.Trim()).ToArray();
            string Cell(int i) => i < cells.Length ? cells[i] : "";
            if (cells[0].Length == 0 || cells[0].Length > 100) { skipped++; continue; }
            if (cells[0].Equals("código", StringComparison.OrdinalIgnoreCase) || cells[0].Equals("codigo", StringComparison.OrdinalIgnoreCase) ||
                cells[0].Equals("code", StringComparison.OrdinalIgnoreCase)) continue;

            var parameter = _model.Parameters.FirstOrDefault(p => p.Code.Equals(cells[0], StringComparison.OrdinalIgnoreCase));
            var isNew = parameter is null;
            parameter ??= new DeviceParameter { Code = cells[0] };
            if (Cell(1).Length > 0) parameter.Address = Cell(1);
            if (ParseType(Cell(2)) is { } type) parameter.Type = type;
            if (ParseNumber(Cell(3)) is { } scale && scale != 0) parameter.Scale = scale;
            if (Cell(4).Length > 0) parameter.Unit = Cell(4);
            if (Cell(5).Length > 0) parameter.Description = Cell(5);
            if (int.TryParse(Cell(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) && group >= 0) parameter.Group = group;
            if (ParseKind(Cell(7)) is { } kind) parameter.Kind = kind;
            if (ParseBool(Cell(8)) is { } writable) parameter.Writable = writable;

            if (isNew)
            {
                _model.Parameters.Add(parameter);
                Parameters.Add(new ParameterRow(parameter, this));
                added++;
            }
            else
            {
                Parameters.First(r => r.Parameter == parameter).Refresh();
                updated++;
            }
        }

        if (added + updated > 0) Changed(nameof(ParametersHeader));
        Status = added + updated == 0
            ? L.T("No había nada que pegar: copia de la hoja de cálculo las columnas código, dirección, tipo, escala, unidad, descripción, grupo, clase y si se escribe.")
            : L.F("Pegados: {0} parámetros nuevos y {1} cambiados.", added, updated) + (skipped > 0 ? " " + L.F("{0} líneas sin código, saltadas.", skipped) : "");
        return added + updated;
    }

    internal static DeviceValueType? ParseType(string text) =>
        Enum.TryParse<DeviceValueType>(text.Trim(), ignoreCase: true, out var type) && Enum.IsDefined(type) ? type
        : text.Trim().ToUpperInvariant() switch { "WORD" or "U16" or "UINT16" => DeviceValueType.UINT, "S16" or "INT16" => DeviceValueType.INT,
            "DWORD" or "U32" or "UINT32" => DeviceValueType.UDINT, "S32" or "INT32" => DeviceValueType.DINT, "FLOAT" or "FLOAT32" => DeviceValueType.REAL, _ => null };

    internal static double? ParseNumber(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static DeviceParameterKind? ParseKind(string text) => text.Trim().ToLowerInvariant() switch
    {
        "setting" or "ajuste" or "a" => DeviceParameterKind.Setting,
        "live" or "en vivo" or "vivo" or "v" => DeviceParameterKind.Live,
        _ => null
    };

    private static bool? ParseBool(string text) => text.Trim().ToLowerInvariant() switch
    {
        "1" or "sí" or "si" or "yes" or "true" or "x" or "r/w" or "rw" => true,
        "0" or "no" or "false" or "r" or "ro" => false,
        _ => null
    };

    // ---------- Fichero ----------

    private void New()
    {
        if (IsDirty && !_services.Confirm(L.T("Hay cambios sin guardar. ¿Empezar uno nuevo igualmente?"))) return;
        Load(NewModel(), null);
        Status = L.T("Fichero nuevo: escribe la marca y el modelo.");
    }

    private void Open()
    {
        if (IsDirty && !_services.Confirm(OpenQuestion)) return;
        if (_services.ChooseFileToOpen() is not { } path) return;
        OpenFile(path);
    }

    /// <summary>Abre un fichero (también uno con problemas: se abre para arreglarlo). False si no es un fichero de dispositivo.</summary>
    public bool OpenFile(string path)
    {
        string text;
        try
        {
            text = _services.ReadText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = L.F("No se pudo leer {0}: {1}", _services.FileName(path), e.Message);
            return false;
        }

        var (model, problems) = DeviceModel.Read(text);
        if (model is null)
        {
            Status = L.F("{0} no es un fichero de dispositivo: {1}", _services.FileName(path), string.Join(" ", problems));
            return false;
        }

        Load(model, path);
        Status = problems.Count == 0 ? L.F("Abierto {0}: {1} parámetros.", _services.FileName(path), model.Parameters.Count)
            : L.F("Abierto {0}, con {1} problemas (abajo).", _services.FileName(path), problems.Count);
        return true;
    }

    private void Save(bool choose)
    {
        Flush();
        var path = _path;
        if (choose || path is null)
        {
            var catalog = _model.CatalogPath().Split('/');
            path = _services.ChooseFileToSave(catalog[1], catalog[0]);
            if (path is null) return;
        }

        try
        {
            _services.WriteText(path, ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = L.F("No se pudo guardar {0}: {1}", _services.FileName(path), e.Message);
            return;
        }

        _path = path;
        IsDirty = false;
        OnPropertyChanged(nameof(Title));
        Status = Problems.Count == 0 ? L.F("Guardado {0}.", _services.FileName(path))
            : L.F("Guardado {0}, con {1} problemas: el programa no lo cargará hasta que se arreglen.", _services.FileName(path), Problems.Count);
    }

    /// <summary>El fichero tal como se guardaría.</summary>
    public string ToJson()
    {
        Flush();
        return _model.ToJson();
    }

    private void Load(DeviceModel model, string? path)
    {
        _model = model;
        _path = path;
        Rebuild();
        IsDirty = false;
        OnPropertyChanged(nameof(Title));
    }

    private static DeviceModel NewModel() => new() { Kind = "" };

    private void Rebuild()
    {
        var save = _model.Protocol.Save;
        (_saveAddress, _saveValue, _saveDescription) = (save?.Address ?? "", save?.Value ?? 0, save?.Description ?? "");
        Groups.Clear();
        foreach (var group in _model.Groups.OrderBy(g => g.Number)) Groups.Add(new GroupRow(group.Number, group.Text, this));
        Parameters.Clear();
        foreach (var parameter in _model.Parameters) Parameters.Add(new ParameterRow(parameter, this));
        Commands.Clear();
        foreach (var command in _model.Commands) Commands.Add(new CommandRow(command, this));
        foreach (var name in new[] { nameof(Brand), nameof(ModelName), nameof(Kind), nameof(Description), nameof(Source), nameof(Verified), nameof(HasDrawing),
                     nameof(RegisterBase), nameof(WordOrder), nameof(MaxRegistersPerRead), nameof(SaveAddress), nameof(SaveValue), nameof(SaveDescription),
                     nameof(RestoreNotes), nameof(GroupOptions), nameof(ParametersHeader), nameof(GroupsHeader), nameof(CommandsHeader), nameof(Title) })
            OnPropertyChanged(name);
        Validate();
    }

    /// <summary>Lo que está en la ventana y no en el modelo de siempre (los grupos y la orden de guardar son registros), al modelo.</summary>
    private void Flush()
    {
        _model.Groups = [.. Groups.Select(g => new DeviceGroup(g.Number, g.Text))];
        _model.Protocol.Save = _saveAddress.Length == 0 ? null : new DeviceSaveCommand(_saveAddress, _saveValue, _saveDescription);
    }

    // ---------- Problemas ----------

    /// <summary>Lo que no vale, con las mismas reglas con las que lo lee el programa (vacío = el fichero vale).</summary>
    public ObservableCollection<string> Problems { get; } = [];

    public bool HasProblems => Problems.Count > 0;

    public string ProblemsHeader => Problems.Count == 0 ? L.T("El fichero vale: el programa lo puede leer.") : L.F("{0} problemas", Problems.Count);

    private void Validate()
    {
        Flush();
        var (_, problems) = DeviceModel.Read(_model.ToJson());
        Problems.Clear();
        foreach (var problem in problems) Problems.Add(problem);
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemsHeader));
    }

    // ---------- Cambios ----------

    private void Set(string value, Action<string> apply, string current, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        value ??= "";
        if (value == current) return;
        apply(value);
        Changed(name!);
    }

    /// <summary>Algo cambió: se marca sin guardar y se vuelve a comprobar.</summary>
    internal void Changed(string property)
    {
        OnPropertyChanged(property);
        IsDirty = true;
        OnPropertyChanged(nameof(Title));
        Validate();
    }
}

/// <summary>Un grupo: número (el orden en el listado) y texto.</summary>
public sealed class GroupRow(int number, string text, MapNodesViewModel owner) : ObservableObject
{
    private int _number = number;
    private string _text = text;

    public int Number
    {
        get => _number;
        set
        {
            if (value == _number || value < 0) return;
            _number = value;
            OnPropertyChanged();
            owner.GroupsChanged();
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text) return;
            _text = value;
            OnPropertyChanged();
            owner.GroupsChanged();
        }
    }
}

/// <summary>
/// Un parámetro en la tabla. Las opciones y los bits se escriben en una línea («0=Adelante; 1=Atrás»); la escala acepta coma o punto. Lo que no
/// se enseña (los decimales que da otro parámetro) se queda como estaba.
/// </summary>
public sealed class ParameterRow(DeviceParameter parameter, MapNodesViewModel owner) : ObservableObject
{
    public DeviceParameter Parameter { get; } = parameter;

    public string Code { get => Parameter.Code; set => Set(value.Trim(), v => Parameter.Code = v, Parameter.Code); }
    public string Address { get => Parameter.Address; set => Set(value.Trim(), v => Parameter.Address = v, Parameter.Address); }
    public string Cip { get => Parameter.Cip; set => Set(value.Trim(), v => Parameter.Cip = v, Parameter.Cip); }

    public static IReadOnlyList<DeviceValueType> Types { get; } = Enum.GetValues<DeviceValueType>();

    public DeviceValueType Type
    {
        get => Parameter.Type;
        set
        {
            if (value == Parameter.Type) return;
            Parameter.Type = value;
            Done();
        }
    }

    /// <summary>La escala en texto (con coma o punto); lo que no es un número no se aplica.</summary>
    public string Scale
    {
        get => Parameter.Scale.ToString("0.##########", CultureInfo.CurrentCulture);
        set
        {
            if (MapNodesViewModel.ParseNumber(value) is not { } scale || scale == Parameter.Scale) { OnPropertyChanged(); return; }
            Parameter.Scale = scale;
            Done();
        }
    }

    public string Unit { get => Parameter.Unit; set => Set(value, v => Parameter.Unit = v, Parameter.Unit); }
    public string Description { get => Parameter.Description; set => Set(value, v => Parameter.Description = v, Parameter.Description); }

    public IReadOnlyList<Option<int>> GroupOptions => owner.GroupOptions;

    public int Group
    {
        get => Parameter.Group;
        set
        {
            if (value == Parameter.Group) return;
            Parameter.Group = value;
            Done();
        }
    }

    public bool Basic
    {
        get => Parameter.Basic;
        set
        {
            if (value == Parameter.Basic) return;
            Parameter.Basic = value;
            Done();
        }
    }

    public static IReadOnlyList<Option<DeviceParameterKind>> Kinds { get; } =
        [new(DeviceParameterKind.Setting, L.T("Ajuste")), new(DeviceParameterKind.Live, L.T("En vivo"))];

    public DeviceParameterKind Kind
    {
        get => Parameter.Kind;
        set
        {
            if (value == Parameter.Kind) return;
            Parameter.Kind = value;
            Done();
        }
    }

    public bool Writable
    {
        get => Parameter.Writable;
        set
        {
            if (value == Parameter.Writable) return;
            Parameter.Writable = value;
            Done();
        }
    }

    public bool Calibration
    {
        get => Parameter.Calibration;
        set
        {
            if (value == Parameter.Calibration) return;
            Parameter.Calibration = value;
            Done();
        }
    }

    public string Options { get => ToText(Parameter.Options); set => SetMap(value, m => Parameter.Options = m, Options); }
    public string Bits { get => ToText(Parameter.Bits); set => SetMap(value, m => Parameter.Bits = m, Bits); }
    public string Note { get => Parameter.Note; set => Set(value, v => Parameter.Note = v, Parameter.Note); }

    /// <summary>Sus decimales los da otro parámetro (se conserva; no se edita aquí).</summary>
    public string DecimalsFrom => Parameter.DecimalsFrom?.From ?? "";

    /// <summary>«0=Adelante; 1=Atrás».</summary>
    public static string ToText(Dictionary<string, string>? map) =>
        map is null ? "" : string.Join("; ", map.Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>De «0=Adelante; 1=Atrás» al diccionario (vacío = sin tabla). Lo que no tiene «=» se salta.</summary>
    public static Dictionary<string, string>? FromText(string text)
    {
        var map = new Dictionary<string, string>();
        foreach (var part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = part.IndexOf('=');
            if (at <= 0) continue;
            map[part[..at].Trim()] = part[(at + 1)..].Trim();
        }

        return map.Count == 0 ? null : map;
    }

    internal void RefreshGroups() => OnPropertyChanged(nameof(GroupOptions));

    internal void Refresh()
    {
        foreach (var name in new[] { nameof(Code), nameof(Address), nameof(Cip), nameof(Type), nameof(Scale), nameof(Unit), nameof(Description), nameof(Group),
                     nameof(Basic), nameof(Kind), nameof(Writable), nameof(Calibration), nameof(Options), nameof(Bits), nameof(Note), nameof(DecimalsFrom) })
            OnPropertyChanged(name);
    }

    private void SetMap(string text, Action<Dictionary<string, string>?> apply, string current)
    {
        var map = FromText(text);
        if (ToText(map) == current) { OnPropertyChanged(nameof(Options)); OnPropertyChanged(nameof(Bits)); return; }
        apply(map);
        Done();
    }

    private void Set(string value, Action<string> apply, string current)
    {
        value ??= "";
        if (value == current) return;
        apply(value);
        Done();
    }

    private void Done()
    {
        Refresh();
        owner.Changed(nameof(MapNodesViewModel.Parameters));
    }
}

/// <summary>Una orden como botón (cero, tara…): texto, dirección, valor y, si la tiene, dónde contesta.</summary>
public sealed class CommandRow(DeviceCommand command, MapNodesViewModel owner) : ObservableObject
{
    public DeviceCommand Command { get; } = command;

    public string Text { get => Command.Text; set => Set(value, v => Command.Text = v, Command.Text); }
    public string Address { get => Command.Address; set => Set(value.Trim(), v => Command.Address = v, Command.Address); }

    public int Value
    {
        get => Command.Value;
        set
        {
            if (value == Command.Value) return;
            Command.Value = value;
            Done();
        }
    }

    public bool ClearFirst
    {
        get => Command.ClearFirst;
        set
        {
            if (value == Command.ClearFirst) return;
            Command.ClearFirst = value;
            Done();
        }
    }

    public bool Confirm
    {
        get => Command.Confirm;
        set
        {
            if (value == Command.Confirm) return;
            Command.Confirm = value;
            Done();
        }
    }

    /// <summary>Dónde contesta (vacío = no se espera respuesta), y los valores de hecha y de error.</summary>
    public string ResponseAddress
    {
        get => Command.Response?.Address ?? "";
        set
        {
            var address = (value ?? "").Trim();
            if (address == ResponseAddress) return;
            Command.Response = address.Length == 0 ? null : new DeviceCommandResponse(address, Command.Response?.Done ?? 0, Command.Response?.Error ?? 0);
            Done();
        }
    }

    public int ResponseDone
    {
        get => Command.Response?.Done ?? 0;
        set
        {
            if (Command.Response is not { } r || value == r.Done) return;
            Command.Response = r with { Done = value };
            Done();
        }
    }

    public int ResponseError
    {
        get => Command.Response?.Error ?? 0;
        set
        {
            if (Command.Response is not { } r || value == r.Error) return;
            Command.Response = r with { Error = value };
            Done();
        }
    }

    public bool HasResponse => Command.Response is not null;

    public string Note { get => Command.Note; set => Set(value, v => Command.Note = v, Command.Note); }

    private void Set(string value, Action<string> apply, string current)
    {
        value ??= "";
        if (value == current) return;
        apply(value);
        Done();
    }

    private void Done()
    {
        foreach (var name in new[] { nameof(Text), nameof(Address), nameof(Value), nameof(ClearFirst), nameof(Confirm), nameof(ResponseAddress), nameof(ResponseDone),
                     nameof(ResponseError), nameof(HasResponse), nameof(Note) })
            OnPropertyChanged(name);
        owner.Changed(nameof(MapNodesViewModel.Commands));
    }
}
