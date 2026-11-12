using System.Text.Json;

namespace FactoryNodes.Devices;

/// <summary>Un modelo que viene dos veces en un catálogo.</summary>
public enum DeviceDuplicates
{
    /// <summary>Una carpeta de ficheros: es un error (dos ficheros del mismo modelo).</summary>
    Problem,

    /// <summary>Se queda el primero: los del proyecto delante de los de este PC (cada proyecto sigue con su versión hasta que se actualiza).</summary>
    FirstWins,

    /// <summary>Se queda el de versión más alta y, a igual versión, el primero.</summary>
    NewestWins
}

/// <summary>
/// El catálogo de dispositivos: los modelos (un JSON cada uno, en <c>library/devices/&lt;marca&gt;/&lt;modelo&gt;.json</c>), por
/// su clave («Omron MX2»), y sus traducciones (<c>&lt;modelo&gt;.&lt;idioma&gt;.json</c>, <see cref="DeviceTranslation"/>). Un fichero que no vale
/// no entra y queda en <see cref="Problems"/>. El texto de cada modelo se guarda tal cual, para pasárselo a la Factory Box.
/// <see cref="Find"/> da el modelo con los textos del idioma elegido para él (<see cref="Choose"/>) o, sin elegir, del preferido
/// (<see cref="PreferredLanguage"/>); si no hay traducción a ese idioma, en el del fichero base.
/// </summary>
public sealed class DeviceCatalog
{
    private readonly Dictionary<string, (DeviceModel Model, string Json)> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, (DeviceTranslation Translation, string Json)>> _translations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _chosen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceModel> _shown = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _problems = [];
    private string _preferred = "";

    public static DeviceCatalog Empty { get; } = new([]);

    /// <param name="files">Nombre (para los avisos) y texto de cada fichero: modelos y traducciones, en cualquier orden.</param>
    /// <param name="duplicates">Qué se hace con un modelo o una traducción que viene dos veces (varios orígenes, PENDIENTES 200).</param>
    public DeviceCatalog(IEnumerable<(string Name, string Json)> files, DeviceDuplicates duplicates = DeviceDuplicates.Problem)
    {
        foreach (var (name, json) in files)
        {
            if (DeviceTranslation.Looks(json))
            {
                var (translation, bad) = DeviceTranslation.Parse(json);
                if (translation is null)
                {
                    _problems.Add($"{name}: {string.Join(" ", bad)}");
                    continue;
                }

                if (!_translations.TryGetValue(translation.Key, out var languages))
                    _translations[translation.Key] = languages = new(StringComparer.OrdinalIgnoreCase);
                var language = translation.Language.Trim();
                if (languages.TryAdd(language, (translation, json))) continue;
                if (duplicates == DeviceDuplicates.Problem) _problems.Add($"{name}: la traducción «{language}» de «{translation.Key}» ya está en otro fichero.");
                else if (duplicates == DeviceDuplicates.NewestWins && translation.Version > languages[language].Translation.Version) languages[language] = (translation, json);
                continue;
            }

            var (model, problems) = DeviceModel.Parse(json);
            if (model is null) _problems.Add($"{name}: {string.Join(" ", problems)}");
            else if (_models.TryAdd(model.Key, (model, json))) continue;
            else if (duplicates == DeviceDuplicates.Problem) _problems.Add($"{name}: «{model.Key}» ya está en otro fichero.");
            else if (duplicates == DeviceDuplicates.NewestWins && model.Version > _models[model.Key].Model.Version) _models[model.Key] = (model, json);
        }
    }

    /// <summary>El idioma en que se quieren los textos si no se ha elegido otro para un modelo (el de la pantalla: «es», «de»…).</summary>
    public string PreferredLanguage
    {
        get => _preferred;
        set
        {
            _preferred = (value ?? "").Trim();
            _shown.Clear();
        }
    }

    /// <summary>Los modelos, con los textos del idioma de cada uno (<see cref="Find"/>).</summary>
    public IReadOnlyList<DeviceModel> Models =>
        _models.Keys.Select(k => Find(k)!).OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> Problems => _problems;

    /// <summary>El modelo, con los textos del idioma elegido para él o del preferido; null si no está.</summary>
    public DeviceModel? Find(string key)
    {
        if (!_models.TryGetValue(key, out var entry)) return null;
        if (_shown.TryGetValue(key, out var shown)) return shown;
        var language = LanguageOf(key);
        shown = !Same(language, entry.Model.Language) && _translations.TryGetValue(key, out var languages) && languages.TryGetValue(language, out var t)
            ? t.Translation.Apply(entry.Model)
            : entry.Model;
        return _shown[key] = shown;
    }

    /// <summary>El modelo como está en su fichero, sin traducir.</summary>
    public DeviceModel? Base(string key) => _models.TryGetValue(key, out var m) ? m.Model : null;

    /// <summary>Los idiomas en que se puede ver ese modelo: el de su fichero (el primero; vacío si no lo dice) y los de sus traducciones.</summary>
    public IReadOnlyList<string> Languages(string key)
    {
        if (!_models.TryGetValue(key, out var entry)) return [];
        var list = new List<string> { entry.Model.Language.Trim().ToLowerInvariant() };
        if (_translations.TryGetValue(key, out var languages))
            list.AddRange(languages.Keys.Select(l => l.ToLowerInvariant()).Where(l => !list.Contains(l)).Order(StringComparer.Ordinal));
        return list;
    }

    /// <summary>El idioma en que se enseña ese modelo ahora: el elegido, el preferido si lo tiene o, si no, el de su fichero.</summary>
    public string LanguageOf(string key)
    {
        var available = Languages(key);
        if (available.Count == 0) return "";
        if (_chosen.TryGetValue(key, out var chosen) && available.Contains(chosen, StringComparer.OrdinalIgnoreCase)) return chosen.ToLowerInvariant();
        var preferred = Short(_preferred);
        return available.FirstOrDefault(l => Same(l, preferred) || Same(Short(l), preferred)) ?? available[0];
    }

    /// <summary>Elige el idioma de los textos de un modelo (autor, 2026-11-12: «pueden haber descargado varios idiomas y desear uno u otro»); vacío = el preferido.</summary>
    public void Choose(string key, string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) _chosen.Remove(key);
        else _chosen[key] = language.Trim().ToLowerInvariant();
        _shown.Remove(key);
    }

    /// <summary>Lo elegido a mano para cada modelo (para recordarlo).</summary>
    public IReadOnlyDictionary<string, string> Chosen => _chosen;

    /// <summary>El texto del fichero de ese modelo (lo que guarda el proyecto y lo que viaja a la caja), o null si no está.</summary>
    public string? JsonOf(string key) => _models.TryGetValue(key, out var m) ? m.Json : null;

    /// <summary>El texto de los ficheros de traducción de ese modelo.</summary>
    public IReadOnlyList<string> TranslationsJsonOf(string key) =>
        _translations.TryGetValue(key, out var languages) ? [.. languages.OrderBy(l => l.Key, StringComparer.OrdinalIgnoreCase).Select(l => l.Value.Json)] : [];

    /// <summary>La versión de la traducción de ese modelo a ese idioma, o null si no la hay.</summary>
    public int? TranslationVersion(string key, string language) =>
        _translations.TryGetValue(key, out var languages) && languages.TryGetValue(language, out var t) ? t.Translation.Version : null;

    /// <summary>
    /// El texto de los modelos que usan esos dispositivos habilitados (lo que viaja a la Factory Box): el fichero principal de cada uno y,
    /// detrás, sus traducciones, para que la caja enseñe los textos en su idioma. Un firmware de antes no entiende las traducciones: las apunta
    /// en su registro como ficheros que no valen y sigue con el fichero principal.
    /// </summary>
    public IReadOnlyList<string> JsonFor(IEnumerable<ProjectDevice> devices) =>
        devices.Where(d => d.Enabled).Select(d => d.ModelKey).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(_models.ContainsKey).SelectMany(k => new[] { _models[k].Json }.Concat(TranslationsJsonOf(k))).ToList();

    /// <summary>Lee el array que llega a la caja (<c>dispositivos.json</c>).</summary>
    public static DeviceCatalog FromArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new DeviceCatalog(document.RootElement.EnumerateArray().Select((e, i) => ($"modelo {i + 1}", e.GetRawText())));
    }

    private static bool Same(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Short(string language) => language.Trim().Length > 2 ? language.Trim()[..2] : language.Trim();
}
