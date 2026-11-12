using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactoryNodes.Devices;

/// <summary>De qué modelo es una traducción.</summary>
public sealed class DeviceTranslationTarget
{
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
}

/// <summary>Los textos de un parámetro en otro idioma. Lo que falta (null o vacío) se queda como en el fichero base.</summary>
public sealed class DeviceParameterTexts
{
    public string? Description { get; set; }
    public string? Note { get; set; }
    public string? Unit { get; set; }
    public Dictionary<string, string>? Options { get; set; }
    public Dictionary<string, string>? Bits { get; set; }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrEmpty(Description) && string.IsNullOrEmpty(Note) && string.IsNullOrEmpty(Unit)
                           && Options is not { Count: > 0 } && Bits is not { Count: > 0 };
}

/// <summary>Los textos de una orden en otro idioma.</summary>
public sealed class DeviceCommandTexts
{
    public string? Text { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// La traducción de un fichero de dispositivo (autor, 2026-11-12: «un archivo por idioma… y un campo interno indicando el idioma»; se acordó
/// así: el fichero base con su idioma, <see cref="DeviceModel.Language"/>, y las traducciones aparte): <c>&lt;marca&gt;/&lt;modelo&gt;.&lt;idioma&gt;.json</c>,
/// <b>solo textos</b>, enlazados por el código de cada parámetro. Nunca direcciones, tipos ni escalas: lo que dice cómo se habla con el equipo
/// está en un solo sitio y una traducción no lo puede cambiar. Lo que una traducción no trae se enseña en el idioma del fichero base.
/// </summary>
public sealed class DeviceTranslation
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>El modelo que traduce (marca y modelo, como en su fichero base).</summary>
    public DeviceTranslationTarget TranslationOf { get; set; } = new();

    /// <summary>El idioma de estos textos («es», «de»…).</summary>
    public string Language { get; set; } = "";

    /// <summary>La versión de esta traducción (1, 2, 3…).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Quién la ha hecho (CC-BY 4.0). Vacío = FactoryNodes.</summary>
    public string Author { get; set; } = "";

    public string? Kind { get; set; }
    public string? Description { get; set; }
    public string? RestoreNotes { get; set; }
    public string? SaveDescription { get; set; }

    /// <summary>El texto de cada grupo, por su número («1»).</summary>
    public Dictionary<string, string> Groups { get; set; } = [];

    /// <summary>Los textos de cada parámetro, por su código (sin distinguir mayúsculas al aplicarlos).</summary>
    public Dictionary<string, DeviceParameterTexts> Parameters { get; set; } = [];

    /// <summary>Los textos de cada orden, por su texto en el fichero base.</summary>
    public Dictionary<string, DeviceCommandTexts> Commands { get; set; } = [];

    [JsonIgnore]
    public string Key => DeviceModel.KeyOf(TranslationOf.Brand, TranslationOf.Model);

    /// <summary>Dónde va en el catálogo: junto al fichero base, con el idioma antes de la extensión (<c>omron/MX2.de.json</c>).</summary>
    public string CatalogPath()
    {
        var model = new DeviceModel { Brand = TranslationOf.Brand, Model = TranslationOf.Model }.CatalogPath();
        return model[..^".json".Length] + "." + Language.Trim().ToLowerInvariant() + ".json";
    }

    public string ToJson() => JsonSerializer.Serialize(this, DeviceModel.WriteJson).ReplaceLineEndings("\n") + "\n";

    /// <summary>Es el texto de una traducción (lleva <c>translationOf</c>) y no el de un modelo: para no leerla como modelo.</summary>
    public static bool Looks(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("translationOf", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Lee una traducción y dice qué no vale. Con algún problema, sin traducción.</summary>
    public static (DeviceTranslation? Translation, IReadOnlyList<string> Problems) Parse(string json)
    {
        DeviceTranslation? translation;
        try
        {
            translation = JsonSerializer.Deserialize<DeviceTranslation>(json, DeviceModel.Json);
        }
        catch (JsonException ex)
        {
            return (null, [$"No es un JSON válido: {ex.Message}"]);
        }

        if (translation is null) return (null, ["Vacío."]);
        var problems = new List<string>();
        if (translation.TranslationOf.Brand.Trim().Length == 0 || translation.TranslationOf.Model.Trim().Length == 0)
            problems.Add("Faltan la marca o el modelo que traduce.");
        var language = translation.Language.Trim();
        if (language.Length is < 2 or > 8 || !language.All(c => char.IsAsciiLetter(c) || c == '-'))
            problems.Add("Falta el idioma («es», «de»…).");
        if (translation.Version < 1) problems.Add("La versión de la traducción, de 1 en adelante.");
        return (problems.Count == 0 ? translation : null, problems);
    }

    /// <summary>
    /// El modelo con los textos de esta traducción: una copia; el original no cambia. Solo cambian textos (qué es, descripción, notas, grupos,
    /// descripción, nota, unidad, opciones y bits de cada parámetro, texto y nota de cada orden). De las opciones y los bits solo se traducen
    /// los valores que el fichero base ya tiene: una traducción no añade ni quita ninguno.
    /// </summary>
    public DeviceModel Apply(DeviceModel model)
    {
        var copy = JsonSerializer.Deserialize<DeviceModel>(JsonSerializer.Serialize(model, DeviceModel.Json), DeviceModel.Json)!;
        copy.Language = Language.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(Kind)) copy.Kind = Kind;
        if (!string.IsNullOrWhiteSpace(Description)) copy.Description = Description;
        if (!string.IsNullOrWhiteSpace(RestoreNotes)) copy.Protocol.RestoreNotes = RestoreNotes;
        if (!string.IsNullOrWhiteSpace(SaveDescription) && copy.Protocol.Save is { } save) copy.Protocol.Save = save with { Description = SaveDescription };
        copy.Groups = [.. copy.Groups.Select(g => Groups.TryGetValue(g.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), out var text)
                                                  && !string.IsNullOrWhiteSpace(text) ? g with { Text = text } : g)];
        var texts = new Dictionary<string, DeviceParameterTexts>(Parameters, StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in copy.Parameters)
        {
            if (!texts.TryGetValue(parameter.Code, out var t)) continue;
            if (!string.IsNullOrWhiteSpace(t.Description)) parameter.Description = t.Description;
            if (!string.IsNullOrWhiteSpace(t.Note)) parameter.Note = t.Note;
            if (!string.IsNullOrWhiteSpace(t.Unit)) parameter.Unit = t.Unit;
            Replace(parameter.Options, t.Options);
            Replace(parameter.Bits, t.Bits);
        }

        foreach (var command in copy.Commands)
        {
            if (!Commands.TryGetValue(command.Text, out var t)) continue;
            if (!string.IsNullOrWhiteSpace(t.Note)) command.Note = t.Note;
            if (!string.IsNullOrWhiteSpace(t.Text)) command.Text = t.Text;
        }

        return copy;
    }

    private static void Replace(Dictionary<string, string>? target, Dictionary<string, string>? texts)
    {
        if (target is null || texts is null) return;
        foreach (var key in target.Keys.ToList())
            if (texts.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text)) target[key] = text;
    }

    /// <summary>
    /// La traducción que lleva de <paramref name="original"/> a <paramref name="translated"/> (el mismo modelo con los textos en otro idioma):
    /// solo los textos que cambian. Para sacar la traducción de un fichero que ya estaba en otro idioma, o de lo escrito en un editor.
    /// </summary>
    public static DeviceTranslation Between(DeviceModel original, DeviceModel translated, string language)
    {
        static string? Changed(string before, string after) => after != before && after.Trim().Length > 0 ? after : null;
        static Dictionary<string, string>? Map(Dictionary<string, string>? before, Dictionary<string, string>? after)
        {
            if (before is null || after is null) return null;
            var map = before.Where(kv => after.TryGetValue(kv.Key, out var text) && text != kv.Value && text.Trim().Length > 0)
                .ToDictionary(kv => kv.Key, kv => after[kv.Key]);
            return map.Count > 0 ? map : null;
        }

        var translation = new DeviceTranslation
        {
            TranslationOf = new DeviceTranslationTarget { Brand = original.Brand.Trim(), Model = original.Model.Trim() },
            Language = language.Trim().ToLowerInvariant(),
            Kind = Changed(original.Kind, translated.Kind),
            Description = Changed(original.Description, translated.Description),
            RestoreNotes = Changed(original.Protocol.RestoreNotes, translated.Protocol.RestoreNotes),
            SaveDescription = original.Protocol.Save is { } a && translated.Protocol.Save is { } b ? Changed(a.Description, b.Description) : null
        };
        foreach (var group in original.Groups)
            if (translated.Groups.FirstOrDefault(g => g.Number == group.Number) is { } other && Changed(group.Text, other.Text) is { } text)
                translation.Groups[group.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)] = text;
        foreach (var parameter in original.Parameters)
        {
            if (translated.Parameter(parameter.Code) is not { } other) continue;
            var texts = new DeviceParameterTexts
            {
                Description = Changed(parameter.Description, other.Description), Note = Changed(parameter.Note, other.Note),
                Unit = Changed(parameter.Unit, other.Unit), Options = Map(parameter.Options, other.Options), Bits = Map(parameter.Bits, other.Bits)
            };
            if (!texts.IsEmpty) translation.Parameters[parameter.Code] = texts;
        }

        for (var i = 0; i < original.Commands.Count && i < translated.Commands.Count; i++)
        {
            var (before, after) = (original.Commands[i], translated.Commands[i]);
            var texts = new DeviceCommandTexts { Text = Changed(before.Text, after.Text), Note = Changed(before.Note, after.Note) };
            if (texts.Text is not null || texts.Note is not null) translation.Commands[before.Text] = texts;
        }

        return translation;
    }
}
