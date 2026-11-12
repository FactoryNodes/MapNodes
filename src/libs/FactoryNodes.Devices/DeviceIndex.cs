using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FactoryNodes.Devices;

/// <summary>Una traducción de un modelo en el índice: su idioma, dónde está y su huella.</summary>
public sealed class DeviceIndexTranslation
{
    public string Language { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Author { get; set; } = "";
}

/// <summary>Un modelo en el índice de la web: lo justo para elegirlo (tipo, marca, modelo) y bajarlo (dónde está y su huella).</summary>
public sealed class DeviceIndexEntry
{
    public string Type { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Verified { get; set; }
    public string Author { get; set; } = "";
    public int Parameters { get; set; }

    /// <summary>
    /// Dónde está su paquete, desde la carpeta del índice: <c>omron/MX2.zip</c> (<see cref="DevicePackage"/>: el fichero principal y sus
    /// traducciones, juntos).
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>SHA-256 (hexadecimal) del paquete, byte a byte.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>El idioma de los textos del fichero base.</summary>
    public string Language { get; set; } = "";

    /// <summary>Sus traducciones a otros idiomas (ficheros aparte, solo textos).</summary>
    public List<DeviceIndexTranslation> Translations { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => DeviceModel.KeyOf(Brand, Model);
}

/// <summary>
/// El índice de los ficheros de dispositivo publicados (autor, 2026-11-12, PENDIENTES 200: «podríamos tener en la web los json, y que cada uno
/// se descargue el que crea conveniente… así no van en cada exe»): <c>index.json</c> con los tipos y una línea por modelo, y su firma al lado
/// (<c>index.json.sig</c>). Se firma solo el índice: lleva la huella de cada fichero, así que un fichero cambiado por el camino no cuadra
/// (<see cref="Matches"/>). Un modelo dice qué registros se escriben al restaurar y qué mandan las órdenes: no se usa uno que no venga firmado.
/// ECDSA P-256 con SHA-256, firma DER en base64 (como las colecciones del catálogo de elementos: la misma clave, <c>build/sign-catalog-sets.ps1</c>).
/// </summary>
public sealed class DeviceIndex
{
    public const string FileName = "index.json", SignatureFileName = "index.json.sig";

    /// <summary>La licencia de los ficheros compartidos (autor, 2026-11-12).</summary>
    public const string SharedLicense = "CC-BY-4.0";

    /// <summary>La clave pública de FactoryNodes (la del catálogo de elementos; la privada está fuera del repositorio).</summary>
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEdeXtrirqXAuzzVa+w3IxvJ4P++ST
        kJqK4RqV5QWQEeJuqNKNSyZwkemD/WGr2ZVGDMD0T6Ocq2VO81gtvCQDNg==
        -----END PUBLIC KEY-----
        """;

    public int SchemaVersion { get; set; } = 1;

    /// <summary>Cuándo se hizo (ISO, UTC).</summary>
    public string Date { get; set; } = "";

    public string License { get; set; } = SharedLicense;
    public List<DeviceType> Types { get; set; } = [];
    public List<DeviceIndexEntry> Models { get; set; } = [];

    public DeviceIndexEntry? Find(string key) => Models.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>El índice de esos ficheros (los que no valen se dicen en <paramref name="problems"/> y no entran), por tipo, marca y modelo.</summary>
    public static DeviceIndex Build(IEnumerable<(string Name, string Json)> files, DeviceTypes types, DateTimeOffset now, out IReadOnlyList<string> problems) =>
        Build(files, types, now, out problems, out _);

    /// <summary>
    /// El índice y el paquete de cada modelo (<paramref name="packages"/>: su sitio, <c>omron/MX2.zip</c>, y sus bytes): lo que se sube a la web.
    /// </summary>
    public static DeviceIndex Build(IEnumerable<(string Name, string Json)> files, DeviceTypes types, DateTimeOffset now, out IReadOnlyList<string> problems,
        out IReadOnlyDictionary<string, byte[]> packages)
    {
        var bad = new List<string>();
        var zips = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        packages = zips;
        var bases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var extras = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var index = new DeviceIndex { Date = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"), Types = [.. types.All] };
        var all = files.ToList();
        foreach (var (name, json) in all.Where(f => !DeviceTranslation.Looks(f.Json)))
        {
            var (model, modelProblems) = DeviceModel.Parse(json);
            if (model is null) bad.Add($"{name}: {string.Join(" ", modelProblems)}");
            else if (index.Find(model.Key) is not null) bad.Add($"{name}: «{model.Key}» ya está en otro fichero.");
            else if (model.Type.Length > 0 && types.Find(model.Type) is null) bad.Add($"{name}: el tipo «{model.Type}» no está en la lista de tipos.");
            else
                index.Models.Add(new DeviceIndexEntry
                {
                    Type = types.KeyOf(model.Type), Brand = model.Brand.Trim(), Model = model.Model.Trim(), Kind = model.Kind, Description = model.Description,
                    Version = model.Version, Verified = model.Verified, Author = model.Author, Parameters = model.Parameters.Count,
                    Path = DevicePackage.PathOf(model), Language = model.Language.Trim().ToLowerInvariant()
                });
            if (model is not null) bases.TryAdd(model.Key, json);
        }

        // las traducciones, cada una con su modelo
        foreach (var (name, json) in all.Where(f => DeviceTranslation.Looks(f.Json)))
        {
            var (translation, translationProblems) = DeviceTranslation.Parse(json);
            if (translation is null) bad.Add($"{name}: {string.Join(" ", translationProblems)}");
            else if (index.Find(translation.Key) is not { } entry) bad.Add($"{name}: traduce «{translation.Key}», que no está.");
            else if (entry.Language.Equals(translation.Language.Trim(), StringComparison.OrdinalIgnoreCase)
                     || entry.Translations.Any(t => t.Language.Equals(translation.Language.Trim(), StringComparison.OrdinalIgnoreCase)))
                bad.Add($"{name}: «{translation.Key}» ya tiene los textos en «{translation.Language}».");
            else
            {
                entry.Translations.Add(new DeviceIndexTranslation
                {
                    Language = translation.Language.Trim().ToLowerInvariant(), Version = translation.Version, Author = translation.Author
                });
                if (!extras.TryGetValue(entry.Key, out var list)) extras[entry.Key] = list = [];
                list.Add(json);
            }
        }

        // el paquete de cada modelo y su huella
        foreach (var entry in index.Models)
        {
            entry.Translations = [.. entry.Translations.OrderBy(t => t.Language, StringComparer.Ordinal)];
            var package = DevicePackage.Pack(bases[entry.Key], extras.GetValueOrDefault(entry.Key) ?? [], out _)!;
            zips[entry.Path] = package;
            entry.Sha256 = Hash(package);
        }

        index.Models = [.. index.Models.OrderBy(m => m.Type, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Model, StringComparer.OrdinalIgnoreCase)];
        problems = bad;
        return index;
    }

    public string ToJson() => JsonSerializer.Serialize(this, DeviceModel.WriteJson).ReplaceLineEndings("\n") + "\n";

    /// <summary>Lee un índice; null si no es uno.</summary>
    public static DeviceIndex? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DeviceIndex>(json, DeviceModel.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>El índice si su firma es la de FactoryNodes; null si no (sin firma, cambiado, o no es un índice).</summary>
    public static DeviceIndex? ParseSigned(string json, string? signature, string publicKeyPem = PublicKeyPem) =>
        Verify(json, signature, publicKeyPem) ? Parse(json) : null;

    /// <summary>
    /// Lo que trae el paquete de ese modelo, si es el que dice el índice (su huella cuadra, es de ese modelo y no trae nada de otro); null si no.
    /// </summary>
    public static IReadOnlyList<DevicePackageFile>? Open(DeviceIndexEntry entry, byte[] package) =>
        Hash(package).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) && DevicePackage.Unpack(package, out _) is { } files
        && DeviceModel.Parse(files[0].Json).Model is { } model && model.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)
            ? files : null;

    /// <summary>Lo que se firma y de lo que se saca la huella: el texto con LF y sin BOM, en UTF-8 (git cambia los saltos al sacar el fichero).</summary>
    public static byte[] Canonical(string text) => Encoding.UTF8.GetBytes(text.TrimStart('﻿').Replace("\r\n", "\n"));

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Canonical(text))).ToLowerInvariant();

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>La firma (base64) de un texto con la clave privada (PEM).</summary>
    public static string Sign(string text, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.SignData(Canonical(text), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }

    /// <summary>La firma es buena para ese texto. Sin firma, o mal formada: no.</summary>
    public static bool Verify(string text, string? signature, string publicKeyPem = PublicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            return key.VerifyData(Canonical(text), Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
