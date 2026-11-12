using System.Text;

namespace FactoryNodes.Devices;

/// <summary>Por qué no se ha podido bajar algo del catálogo de la web.</summary>
public enum DeviceDownloadFailure
{
    /// <summary>No se llega a la web (sin internet, el servidor no contesta).</summary>
    Unreachable,

    /// <summary>El índice no lleva la firma de FactoryNodes (o no es un índice): no se usa.</summary>
    BadIndex,

    /// <summary>El paquete no es el que dice el índice (su huella no cuadra, o no es de ese modelo): no se usa.</summary>
    BadFile
}

public sealed class DeviceDownloadException(DeviceDownloadFailure reason, string detail = "") : Exception($"{reason}: {detail}")
{
    public DeviceDownloadFailure Reason { get; } = reason;
    public string Detail { get; } = detail;
}

/// <summary>
/// Bajar del catálogo de la web (PENDIENTES 200): el índice firmado y el paquete de cada modelo (un zip con su fichero principal y sus
/// traducciones, <see cref="DevicePackage"/>), comprobado con su huella. Sin red aquí: la app da <paramref name="get"/> (los bytes de una
/// dirección; FactoryNodes y MapNodes, con <c>HttpClient</c>). Nada que no venga firmado se devuelve.
/// </summary>
public sealed class DeviceDownloads(Func<string, CancellationToken, Task<byte[]>> get, string baseUrl = DeviceDownloads.DefaultBaseUrl)
{
    /// <summary>Donde los deja <c>build\publish-devices.ps1</c>.</summary>
    public const string DefaultBaseUrl = "https://www.factorynodes.com/devices/";

    /// <summary>La dirección de un fichero del catálogo (<c>schneider electric/ATV320.zip</c>: cada trozo, escapado).</summary>
    public string UrlOf(string path) =>
        baseUrl.TrimEnd('/') + "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    /// <summary>El índice, con la firma de FactoryNodes comprobada.</summary>
    public async Task<DeviceIndex> IndexAsync(CancellationToken cancellation = default)
    {
        var json = Encoding.UTF8.GetString(await GetAsync(DeviceIndex.FileName, cancellation));
        var signature = Encoding.UTF8.GetString(await GetAsync(DeviceIndex.SignatureFileName, cancellation));
        return DeviceIndex.ParseSigned(json, signature) ?? throw new DeviceDownloadException(DeviceDownloadFailure.BadIndex);
    }

    /// <summary>
    /// Lo que trae el paquete de un modelo del índice, comprobado con su huella: el fichero principal el primero y luego sus traducciones, cada
    /// uno con su sitio en el catálogo.
    /// </summary>
    public async Task<IReadOnlyList<DevicePackageFile>> PackageAsync(DeviceIndexEntry entry, CancellationToken cancellation = default)
    {
        // el índice viene de fuera: su ruta no sale de la carpeta del catálogo
        if (entry.Path.Contains("..", StringComparison.Ordinal) || entry.Path.Contains((char)92) || entry.Path.StartsWith('/') || entry.Path.Contains(':'))
            throw new DeviceDownloadException(DeviceDownloadFailure.BadFile, entry.Path);
        var package = await GetAsync(entry.Path, cancellation);
        return DeviceIndex.Open(entry, package) ?? throw new DeviceDownloadException(DeviceDownloadFailure.BadFile, entry.Key);
    }

    private async Task<byte[]> GetAsync(string path, CancellationToken cancellation)
    {
        try
        {
            return await get(UrlOf(path), cancellation);
        }
        catch (Exception e) when (e is not OperationCanceledException and not DeviceDownloadException)
        {
            throw new DeviceDownloadException(DeviceDownloadFailure.Unreachable, e.Message);
        }
    }
}
