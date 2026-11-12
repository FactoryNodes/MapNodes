using System.Buffers.Binary;
using System.Net.Sockets;
using FactoryNodes.Devices;

namespace FactoryNodes.Modbus;

/// <summary>Una excepción Modbus del equipo (función rechazada, dirección que no existe…) o un fallo de la conexión.</summary>
public sealed class ModbusException(string message) : Exception(message);

/// <summary>
/// Cliente Modbus TCP (autor, 2026-10-13): funciones 03 (leer holding registers) y 16 (escribir registros), una petición a
/// la vez, con su tiempo de espera. Se conecta al usarlo y, si la conexión se cae, la próxima petición vuelve a conectar.
/// Para un equipo RS-485 detrás de una pasarela Modbus TCP/RTU, el número de esclavo es el del equipo.
/// </summary>
public sealed class ModbusTcpClient(string host, int port = 502) : IModbusClient, IAsyncDisposable
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private TcpClient? _tcp;
    private ushort _transaction;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    public async Task<ushort[]> ReadHoldingRegistersAsync(byte unit, ushort address, ushort count, CancellationToken cancellation = default)
    {
        if (count is < 1 or > 125) throw new ArgumentOutOfRangeException(nameof(count), "Entre 1 y 125 registros por lectura.");
        var pdu = new byte[5];
        pdu[0] = 0x03;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1), address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3), count);
        var answer = await RequestAsync(unit, pdu, cancellation);
        if (answer.Length < 2 || answer[1] != count * 2 || answer.Length < 2 + count * 2)
            throw new ModbusException($"Respuesta de lectura que no cuadra ({answer.Length} bytes para {count} registros).");
        var registers = new ushort[count];
        for (var i = 0; i < count; i++) registers[i] = BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(2 + i * 2));
        return registers;
    }

    public async Task WriteMultipleRegistersAsync(byte unit, ushort address, ushort[] values, CancellationToken cancellation = default)
    {
        if (values.Length is < 1 or > 123) throw new ArgumentOutOfRangeException(nameof(values), "Entre 1 y 123 registros por escritura.");
        var pdu = new byte[6 + values.Length * 2];
        pdu[0] = 0x10;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1), address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3), (ushort)values.Length);
        pdu[5] = (byte)(values.Length * 2);
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(6 + i * 2), values[i]);
        await RequestAsync(unit, pdu, cancellation);
    }

    private async Task<byte[]> RequestAsync(byte unit, byte[] pdu, CancellationToken cancellation)
    {
        await _one.WaitAsync(cancellation);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(Timeout);
            try
            {
                return await ExchangeAsync(unit, pdu, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                Drop();
                throw new ModbusException($"{host}:{port} no contesta (esclavo {unit}, {Timeout.TotalSeconds:0.#} s).");
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                Drop();
                throw new ModbusException($"Sin conexión con {host}:{port}: {ex.Message}");
            }
        }
        finally
        {
            _one.Release();
        }
    }

    private async Task<byte[]> ExchangeAsync(byte unit, byte[] pdu, CancellationToken cancellation)
    {
        if (_tcp is not { Connected: true })
        {
            Drop();
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(host, port, cancellation);
        }

        var stream = _tcp.GetStream();
        var id = ++_transaction;
        var frame = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, id);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(pdu.Length + 1));
        frame[6] = unit;
        pdu.CopyTo(frame, 7);
        await stream.WriteAsync(frame, cancellation);

        while (true)
        {
            var header = new byte[7];
            await stream.ReadExactlyAsync(header, cancellation);
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
            if (length is < 2 or > 260) throw new IOException($"Trama Modbus de longitud {length}.");
            var body = new byte[length - 1];
            await stream.ReadExactlyAsync(body, cancellation);
            if (BinaryPrimitives.ReadUInt16BigEndian(header) != id) continue;   // respuesta atrasada de otra petición
            if ((body[0] & 0x80) != 0) throw new ModbusException($"El equipo rechaza la petición: {ExceptionText(body.Length > 1 ? body[1] : (byte)0)}.");
            if (body[0] != pdu[0]) throw new ModbusException($"Respuesta de la función {body[0]} a una petición {pdu[0]}.");
            return body;
        }
    }

    public static string ExceptionText(byte code) => code switch
    {
        1 => "función no admitida (01)",
        2 => "dirección que no existe (02)",
        3 => "valor fuera de rango (03)",
        4 => "fallo del equipo (04)",
        6 => "equipo ocupado (06)",
        10 => "la pasarela no encuentra el camino (0A)",
        11 => "el equipo no contesta a la pasarela (0B)",
        _ => $"excepción {code}"
    };

    private void Drop()
    {
        _tcp?.Dispose();
        _tcp = null;
    }

    public ValueTask DisposeAsync()
    {
        Drop();
        _one.Dispose();
        return ValueTask.CompletedTask;
    }
}
