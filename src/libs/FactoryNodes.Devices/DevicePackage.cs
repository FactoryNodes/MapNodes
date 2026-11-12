using System.IO.Compression;
using System.Text;

namespace FactoryNodes.Devices;

/// <summary>Un fichero de un paquete: dónde va en el catálogo (<c>omron/MX2.json</c>, <c>omron/MX2.de.json</c>) y su texto.</summary>
public sealed record DevicePackageFile(string Path, string Json);

/// <summary>
/// El paquete de un modelo (autor, 2026-11-12: «que la descarga tenga el archivo de parámetros… y sus idiomas traducidos, todo comprimido en un
/// zip»): un zip con el fichero principal (<c>&lt;modelo&gt;.json</c>, en el idioma del manual del fabricante) y sus traducciones
/// (<c>&lt;modelo&gt;.&lt;idioma&gt;.json</c>, solo textos). Así viajan siempre juntos y de la misma versión, y el índice lleva una sola huella por
/// modelo. Sin disco: de bytes a bytes. Lo que se desempaqueta viene de fuera: se comprueban el número de ficheros, su tamaño, sus nombres y
/// que todos sean de ese modelo.
/// </summary>
public static class DevicePackage
{
    public const int MaxFiles = 40;
    public const int MaxFileBytes = 4 * 1024 * 1024;
    public const int MaxTotalBytes = 16 * 1024 * 1024;

    // la misma fecha en todas las entradas: el mismo contenido da siempre el mismo zip (y la misma huella)
    private static readonly DateTimeOffset Stamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Dónde va el paquete de un modelo en la web: <c>omron/MX2.zip</c>.</summary>
    public static string PathOf(DeviceModel model) => model.CatalogPath()[..^".json".Length] + ".zip";

    /// <summary>
    /// Empaqueta el fichero principal y sus traducciones (las de otro modelo, o al idioma del principal, no entran: se dicen en
    /// <paramref name="problems"/>). Null si el principal no vale.
    /// </summary>
    public static byte[]? Pack(string modelJson, IEnumerable<string> translations, out IReadOnlyList<string> problems)
    {
        var bad = new List<string>();
        problems = bad;
        var (model, modelProblems) = DeviceModel.Parse(modelJson);
        if (model is null)
        {
            bad.AddRange(modelProblems);
            return null;
        }

        var files = new List<(string Name, string Json)> { (Name(model.CatalogPath()), modelJson) };
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { model.Language.Trim() };
        foreach (var json in translations)
        {
            var (translation, translationProblems) = DeviceTranslation.Parse(json);
            if (translation is null) bad.Add(string.Join(" ", translationProblems));
            else if (!translation.Key.Equals(model.Key, StringComparison.OrdinalIgnoreCase)) bad.Add($"Una traducción es de «{translation.Key}», no de «{model.Key}».");
            else if (!languages.Add(translation.Language.Trim())) bad.Add($"«{model.Key}» ya tiene los textos en «{translation.Language}».");
            else files.Add((Name(translation.CatalogPath()), json));
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, json) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = Stamp;
                using var writer = entry.Open();
                var bytes = Utf8.GetBytes(json.TrimStart('﻿').Replace("\r\n", "\n"));
                writer.Write(bytes, 0, bytes.Length);
            }

        return stream.ToArray();
    }

    /// <summary>
    /// Lo que trae un paquete, con el sitio de cada fichero en el catálogo: el principal el primero y luego sus traducciones. Null (y por
    /// qué) si no es un paquete de un solo modelo: sin fichero principal o con más de uno, con traducciones de otro modelo, con nombres que
    /// no son de fichero, o demasiado grande.
    /// </summary>
    public static IReadOnlyList<DevicePackageFile>? Unpack(byte[] package, out string problem)
    {
        problem = "";
        var texts = new List<string>();
        try
        {
            using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
            if (zip.Entries.Count is 0 or > MaxFiles) return Fail(out problem, "El paquete no trae ficheros, o trae demasiados.");
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                // el nombre no se usa para guardar (el sitio sale del contenido), pero uno raro delata un paquete que no es nuestro
                if (entry.FullName.Contains('/') || entry.FullName.Contains((char)92) || entry.FullName.Contains("..", StringComparison.Ordinal)
                    || !entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return Fail(out problem, $"«{entry.FullName}» no es un fichero de un paquete de dispositivo.");
                if (entry.Length > MaxFileBytes || (total += entry.Length) > MaxTotalBytes) return Fail(out problem, "El paquete es demasiado grande.");
                using var reader = new StreamReader(entry.Open(), Utf8);
                var buffer = new char[MaxFileBytes + 1];
                var read = reader.ReadBlock(buffer, 0, buffer.Length);   // el tamaño que dice el zip puede mentir: se corta al leer
                if (read > MaxFileBytes) return Fail(out problem, "El paquete es demasiado grande.");
                texts.Add(new string(buffer, 0, read));
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
        {
            return Fail(out problem, "No es un zip.");
        }

        var models = texts.Where(t => !DeviceTranslation.Looks(t)).ToList();
        if (models.Count != 1) return Fail(out problem, "El paquete tiene que traer un fichero de dispositivo, y solo uno.");
        var (model, modelProblems) = DeviceModel.Parse(models[0]);
        if (model is null) return Fail(out problem, string.Join(" ", modelProblems));
        var files = new List<DevicePackageFile> { new(model.CatalogPath(), models[0]) };
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { model.Language.Trim() };
        foreach (var json in texts.Where(DeviceTranslation.Looks))
        {
            var (translation, translationProblems) = DeviceTranslation.Parse(json);
            if (translation is null) return Fail(out problem, string.Join(" ", translationProblems));
            if (!translation.Key.Equals(model.Key, StringComparison.OrdinalIgnoreCase))
                return Fail(out problem, $"Una traducción es de «{translation.Key}», no de «{model.Key}».");
            if (!languages.Add(translation.Language.Trim())) return Fail(out problem, $"«{model.Key}» trae dos veces los textos en «{translation.Language}».");
            files.Add(new DevicePackageFile(translation.CatalogPath(), json));
        }

        return files;
    }

    private static string Name(string catalogPath) => catalogPath[(catalogPath.LastIndexOf('/') + 1)..];

    private static IReadOnlyList<DevicePackageFile>? Fail(out string problem, string why)
    {
        problem = why;
        return null;
    }
}
