using System.Collections.ObjectModel;
using System.Windows.Input;
using FactoryNodes.Devices;

namespace FactoryNodes.MapNodes.Presentation;

/// <summary>
/// Los idiomas (autor, 2026-11-12: «un campo interno indicando el idioma»; «en el mapnodes la posibilidad de generar idiomas… sin guardar los
/// parámetros, solo idiomas»): el idioma de los textos del fichero abierto y la pestaña «Traducción», donde se escriben esos textos en otro
/// idioma con el original al lado. La traducción es un fichero aparte (<see cref="DeviceTranslation"/>: solo textos, enlazados por el código de
/// cada parámetro) con su propio guardar; el fichero base no se toca ni hace falta haberlo guardado.
/// </summary>
public sealed partial class MapNodesViewModel
{
    private static readonly string[] LanguageCodes = ["es", "ca", "en", "fr", "de", "pt"];
    private string _translationLanguage = "";
    private string _translationAuthor = "";
    private int _translationVersion = 1;
    private bool _translationDirty;

    private void InitTranslation()
    {
        StartTranslationCommand = new RelayCommand(StartTranslation, () => TranslationLanguage.Length > 0);
        OpenTranslationCommand = new RelayCommand(OpenTranslation);
        SaveTranslationCommand = new RelayCommand(SaveTranslation, () => TranslationLanguage.Length > 0 && TranslationRows.Count > 0);
    }

    private static string LanguageName(string code) => code switch
    {
        "es" => "Castellano", "ca" => "Català", "en" => "English", "fr" => "Français", "de" => "Deutsch", "pt" => "Português", _ => code
    };

    // ---------- El idioma del fichero base ----------

    public IReadOnlyList<Option<string>> LanguageOptions =>
        [new("", L.T("(sin indicar)")), .. LanguageCodes.Select(c => new Option<string>(c, LanguageName(c)))];

    /// <summary>En qué idioma están los textos de este fichero.</summary>
    public string Language
    {
        get => _model.Language.Trim().ToLowerInvariant();
        set
        {
            if (value is null || value == Language) return;
            _model.Language = value;
            Changed(nameof(Language));
            OnPropertyChanged(nameof(TranslationLanguageOptions));
        }
    }

    // ---------- La traducción ----------

    public ICommand StartTranslationCommand { get; private set; } = null!;
    public ICommand OpenTranslationCommand { get; private set; } = null!;
    public ICommand SaveTranslationCommand { get; private set; } = null!;

    /// <summary>Cada texto del fichero con su traducción: lo que se deja vacío se queda en el idioma del fichero base.</summary>
    public ObservableCollection<TranslationRow> TranslationRows { get; } = [];

    /// <summary>Los idiomas a los que se puede traducir: los seis menos el del fichero.</summary>
    public IReadOnlyList<Option<string>> TranslationLanguageOptions =>
        [.. LanguageCodes.Where(c => c != Language).Select(c => new Option<string>(c, LanguageName(c)))];

    public string TranslationLanguage
    {
        get => _translationLanguage;
        set
        {
            if (value is null || value == _translationLanguage) return;
            _translationLanguage = value;
            OnPropertyChanged();
            ((RelayCommand)StartTranslationCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SaveTranslationCommand).RaiseCanExecuteChanged();
        }
    }

    public string TranslationAuthor { get => _translationAuthor; set { _translationAuthor = value ?? ""; OnPropertyChanged(); TranslationTouched(); } }

    public int TranslationVersion { get => _translationVersion; set { _translationVersion = value; OnPropertyChanged(); TranslationTouched(); } }

    /// <summary>Hay textos traducidos sin guardar.</summary>
    public bool IsTranslationDirty => _translationDirty;

    public string TranslationHeader => TranslationRows.Count == 0 ? L.T("Traducción")
        : L.F("Traducción ({0} de {1})", TranslationRows.Count(r => r.Text.Trim().Length > 0), TranslationRows.Count);

    internal void TranslationTouched()
    {
        _translationDirty = true;
        OnPropertyChanged(nameof(IsTranslationDirty));
        OnPropertyChanged(nameof(TranslationHeader));
    }

    /// <summary>
    /// Pone en la lista todos los textos del fichero abierto (como está ahora en la ventana, guardado o no), para traducirlos. Lo ya
    /// traducido se conserva si el texto sigue existiendo.
    /// </summary>
    public void StartTranslation()
    {
        if (TranslationLanguage.Length == 0) return;
        Flush();
        var done = TranslationRows.Where(r => r.Text.Trim().Length > 0).ToDictionary(r => r.Id, r => r.Text);
        TranslationRows.Clear();
        void Add(string id, string what, string original)
        {
            if (original.Trim().Length > 0)
                TranslationRows.Add(new TranslationRow(id, what, original, done.GetValueOrDefault(id, ""), this));
        }

        Add("kind", L.T("Qué es"), _model.Kind);
        Add("description", L.T("Descripción"), _model.Description);
        Add("restoreNotes", L.T("Antes de restaurar"), _model.Protocol.RestoreNotes);
        Add("saveDescription", L.T("Guardar en EEPROM"), _model.Protocol.Save?.Description ?? "");
        foreach (var group in _model.Groups) Add($"g|{group.Number}", L.F("Grupo {0}", group.Number), group.Text);
        foreach (var p in _model.Parameters)
        {
            Add($"p|{p.Code}|d", p.Code, p.Description);
            Add($"p|{p.Code}|u", L.F("{0} · unidad", p.Code), p.Unit);
            foreach (var (value, text) in p.Options ?? []) Add($"p|{p.Code}|o|{value}", L.F("{0} · opción {1}", p.Code, value), text);
            foreach (var (bit, text) in p.Bits ?? []) Add($"p|{p.Code}|b|{bit}", L.F("{0} · bit {1}", p.Code, bit), text);
            Add($"p|{p.Code}|n", L.F("{0} · nota", p.Code), p.Note);
        }

        foreach (var c in _model.Commands)
        {
            Add($"c|{c.Text}|t", L.F("Orden «{0}»", c.Text), c.Text);
            Add($"c|{c.Text}|n", L.F("Orden «{0}» · nota", c.Text), c.Note);
        }

        OnPropertyChanged(nameof(TranslationHeader));
        ((RelayCommand)SaveTranslationCommand).RaiseCanExecuteChanged();
        Status = L.F("{0} textos para traducir al {1}. Lo que dejes vacío se verá en el idioma del fichero.", TranslationRows.Count, LanguageName(TranslationLanguage));
    }

    /// <summary>La traducción como se guardaría: solo lo traducido.</summary>
    public DeviceTranslation BuildTranslation()
    {
        var translation = new DeviceTranslation
        {
            TranslationOf = new DeviceTranslationTarget { Brand = _model.Brand.Trim(), Model = _model.Model.Trim() },
            Language = TranslationLanguage, Version = TranslationVersion, Author = TranslationAuthor.Trim()
        };
        DeviceParameterTexts Of(string code)
        {
            if (!translation.Parameters.TryGetValue(code, out var texts)) translation.Parameters[code] = texts = new DeviceParameterTexts();
            return texts;
        }

        DeviceCommandTexts OfCommand(string text)
        {
            if (!translation.Commands.TryGetValue(text, out var texts)) translation.Commands[text] = texts = new DeviceCommandTexts();
            return texts;
        }

        foreach (var row in TranslationRows.Where(r => r.Text.Trim().Length > 0))
        {
            var id = row.Id.Split('|');
            var text = row.Text.Trim();
            switch (id[0])
            {
                case "kind": translation.Kind = text; break;
                case "description": translation.Description = text; break;
                case "restoreNotes": translation.RestoreNotes = text; break;
                case "saveDescription": translation.SaveDescription = text; break;
                case "g": translation.Groups[id[1]] = text; break;
                case "p" when id[2] == "d": Of(id[1]).Description = text; break;
                case "p" when id[2] == "u": Of(id[1]).Unit = text; break;
                case "p" when id[2] == "n": Of(id[1]).Note = text; break;
                case "p" when id[2] == "o": (Of(id[1]).Options ??= [])[id[3]] = text; break;
                case "p" when id[2] == "b": (Of(id[1]).Bits ??= [])[id[3]] = text; break;
                case "c" when id[2] == "t": OfCommand(id[1]).Text = text; break;
                case "c" when id[2] == "n": OfCommand(id[1]).Note = text; break;
            }
        }

        return translation;
    }

    private void SaveTranslation()
    {
        if (_model.Brand.Trim().Length == 0 || _model.Model.Trim().Length == 0)
        {
            Status = L.T("Antes de guardar la traducción, pon la marca y el modelo del fichero.");
            return;
        }

        var translation = BuildTranslation();
        var place = translation.CatalogPath().Split('/');
        if (_services.ChooseFileToSave(place[1], place[0]) is not { } path) return;
        try
        {
            _services.WriteText(path, translation.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = L.F("No se pudo guardar {0}: {1}", _services.FileName(path), e.Message);
            return;
        }

        _translationDirty = false;
        OnPropertyChanged(nameof(IsTranslationDirty));
        Status = L.F("Guardada la traducción en {0}: solo textos; el fichero del dispositivo no cambia.", _services.FileName(path));
    }

    private void OpenTranslation()
    {
        if (_services.ChooseFileToOpen() is not { } path) return;
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

        LoadTranslation(text, _services.FileName(path));
    }

    /// <summary>Carga una traducción ya hecha para seguir con ella. Tiene que ser del modelo que está abierto.</summary>
    public bool LoadTranslation(string json, string name)
    {
        var (translation, problems) = DeviceTranslation.Looks(json) ? DeviceTranslation.Parse(json) : (null, [L.T("no lleva «translationOf»")]);
        if (translation is null)
        {
            Status = L.F("{0} no es un fichero de traducción: {1}", name, string.Join(" ", problems));
            return false;
        }

        if (!translation.Key.Equals(_model.Key, StringComparison.OrdinalIgnoreCase))
        {
            Status = L.F("{0} es la traducción de {1}, y el fichero abierto es {2}.", name, translation.Key, _model.Key.Trim());
            return false;
        }

        _translationLanguage = translation.Language.Trim().ToLowerInvariant();
        (_translationAuthor, _translationVersion) = (translation.Author, translation.Version);
        TranslationRows.Clear();
        StartTranslation();
        // lo traducido, a su fila: se saca aplicando la traducción al modelo y mirando qué texto ha cambiado
        var translated = translation.Apply(_model);
        string? Now(string id)
        {
            var part = id.Split('|');
            DeviceParameter? P() => translated.Parameter(part[1]);
            var index = part[0] == "c" ? _model.Commands.FindIndex(c => c.Text == part[1]) : -1;
            return part[0] switch
            {
                "kind" => translated.Kind, "description" => translated.Description, "restoreNotes" => translated.Protocol.RestoreNotes,
                "saveDescription" => translated.Protocol.Save?.Description,
                "g" => translated.Groups.FirstOrDefault(g => g.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) == part[1])?.Text,
                "p" when part[2] == "d" => P()?.Description, "p" when part[2] == "u" => P()?.Unit, "p" when part[2] == "n" => P()?.Note,
                "p" when part[2] == "o" => P()?.Options?.GetValueOrDefault(part[3]), "p" when part[2] == "b" => P()?.Bits?.GetValueOrDefault(part[3]),
                "c" when index >= 0 && part[2] == "t" => translated.Commands[index].Text,
                "c" when index >= 0 && part[2] == "n" => translated.Commands[index].Note,
                _ => null
            };
        }

        foreach (var row in TranslationRows)
            if (Now(row.Id) is { } now && now != row.Original) row.Load(now);
        _translationDirty = false;
        foreach (var property in new[] { nameof(TranslationLanguage), nameof(TranslationAuthor), nameof(TranslationVersion), nameof(TranslationHeader), nameof(IsTranslationDirty) })
            OnPropertyChanged(property);
        Status = L.F("Abierta la traducción {0}: {1} de {2} textos traducidos.", name, TranslationRows.Count(r => r.Text.Length > 0), TranslationRows.Count);
        return true;
    }
}

/// <summary>Un texto del fichero con su traducción (vacía = se queda como en el fichero).</summary>
public sealed class TranslationRow(string id, string what, string original, string text, MapNodesViewModel owner) : ObservableObject
{
    private string _text = text;

    /// <summary>Qué texto es, para guardarlo en su sitio (no se enseña).</summary>
    public string Id { get; } = id;

    /// <summary>Qué texto es, para leerlo («C1-01», «C1-01 · opción 2», «Grupo 3»).</summary>
    public string What { get; } = what;

    public string Original { get; } = original;

    public string Text
    {
        get => _text;
        set
        {
            if ((value ?? "") == _text) return;
            _text = value ?? "";
            OnPropertyChanged();
            owner.TranslationTouched();
        }
    }

    internal void Load(string text)
    {
        _text = text;
        OnPropertyChanged(nameof(Text));
    }
}
