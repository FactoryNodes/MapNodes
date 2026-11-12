using System.Reflection;
using System.Text.Json;

namespace FactoryNodes.Devices;

/// <summary>
/// Un tipo de dispositivo (autor, 2026-11-12, PENDIENTES 200: «marca, Tipo (Variador, arrancador, Pesaje, iluminacion…)… para que no sea todo
/// un cajón de sastre»): su clave (la que guarda cada modelo en <see cref="DeviceModel.Type"/>) y su nombre en cada idioma.
/// </summary>
public sealed class DeviceType
{
    public string Key { get; set; } = "";
    public string Es { get; set; } = "";
    public string Ca { get; set; } = "";
    public string En { get; set; } = "";
    public string Fr { get; set; } = "";
    public string De { get; set; } = "";
    public string Pt { get; set; } = "";

    /// <summary>El nombre en ese idioma («ca», «en-GB»…); sin traducción, el castellano.</summary>
    public string Text(string language)
    {
        var text = (language.Length >= 2 ? language[..2].ToLowerInvariant() : "") switch
        {
            "ca" => Ca, "en" => En, "fr" => Fr, "de" => De, "pt" => Pt, _ => Es
        };
        return text.Length > 0 ? text : Es.Length > 0 ? Es : Key;
    }
}

/// <summary>
/// La lista cerrada de tipos de dispositivo: la que lleva la librería (<c>Types.json</c>, incrustado: un tipo nuevo es una línea ahí) y, si el
/// índice de la web trae otra más nueva, esa (<see cref="DeviceIndex.Types"/>): así un programa de antes conoce los tipos nuevos. Un modelo sin
/// tipo, o con uno que no está en la lista, va en «Otros» (<see cref="Other"/>).
/// </summary>
public sealed class DeviceTypes
{
    /// <summary>La clave de «Otros»: los modelos sin tipo o con uno que la lista no tiene.</summary>
    public const string Other = "";

    private static readonly Lazy<DeviceTypes> BuiltInTypes = new(() =>
    {
        using var stream = typeof(DeviceTypes).Assembly.GetManifestResourceStream("FactoryNodes.Devices.Types.json")
                           ?? throw new InvalidOperationException("Falta Types.json en FactoryNodes.Devices.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()) ?? throw new InvalidOperationException("Types.json no vale.");
    });

    private readonly List<DeviceType> _types;

    public DeviceTypes(IEnumerable<DeviceType> types) =>
        _types = types.Where(t => t.Key.Trim().Length > 0).GroupBy(t => t.Key.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

    /// <summary>Los que lleva la librería.</summary>
    public static DeviceTypes BuiltIn => BuiltInTypes.Value;

    public IReadOnlyList<DeviceType> All => _types;

    public DeviceType? Find(string key) => _types.FirstOrDefault(t => t.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>La clave tal como está en la lista, o <see cref="Other"/> si no está.</summary>
    public string KeyOf(string key) => Find(key)?.Key ?? Other;

    /// <summary>El nombre del tipo en ese idioma; <paramref name="other"/> («Otros», ya traducido) si no está en la lista.</summary>
    public string Text(string key, string language, string other) => Find(key)?.Text(language) ?? other;

    /// <summary>Estos y los de <paramref name="more"/> que no estaban (los de un índice más nuevo).</summary>
    public DeviceTypes With(IEnumerable<DeviceType> more) => new(_types.Concat(more));

    /// <summary>Lee <c>{ "types": [ … ] }</c>; null si no vale.</summary>
    public static DeviceTypes? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TypesFile>(json, DeviceModel.Json) is { Types: { } types } ? new DeviceTypes(types) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class TypesFile
    {
        public List<DeviceType>? Types { get; set; }
    }
}
