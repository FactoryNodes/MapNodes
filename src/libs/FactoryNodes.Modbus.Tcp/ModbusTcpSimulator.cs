using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using FactoryNodes.Devices;

namespace FactoryNodes.Modbus;

/// <summary>
/// Un equipo Modbus TCP de mentira (holding registers en memoria; funciones 03, 06 y 16): para los tests y para probar la
/// Factory Box en la oficina sin variadores. Una dirección que no está en <see cref="Registers"/> contesta con la
/// excepción 02, como un equipo de verdad. <see cref="Fill"/> lo llena con los parámetros de un modelo del catálogo.
/// </summary>
public sealed class ModbusTcpSimulator : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accept;

    public ModbusTcpSimulator(int port = 0, IPAddress? address = null)
    {
        _listener = new TcpListener(address ?? IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptAsync();
    }

    public int Port { get; }

    /// <summary>Dirección de la trama → valor.</summary>
    public ConcurrentDictionary<ushort, ushort> Registers { get; } = new();

    /// <summary>Peticiones atendidas (para los tests).</summary>
    public int Requests;

    /// <summary>Pone cada parámetro del modelo con un valor (por defecto, el número de su dirección, recortado).</summary>
    public void Fill(DeviceModel model, Func<DeviceParameter, long>? value = null)
    {
        foreach (var p in model.Parameters.Where(p => p.AddressNumber is not null))
        {
            var registers = DeviceValues.Registers(p, value?.Invoke(p) ?? p.AddressNumber!.Value % 1000, model.Protocol.WordOrder);
            for (var i = 0; i < registers.Length; i++) Registers[(ushort)(model.PduAddress(p) + i)] = registers[i];
        }

        if (model.Protocol.Save is { } save)
            Registers[(ushort)(new DeviceParameter { Address = save.Address }.AddressNumber!.Value - model.Protocol.RegisterBase)] = 0;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = ServeAsync(client);
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
            // parado
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var header = new byte[7];
                    await stream.ReadExactlyAsync(header, _stop.Token);
                    var pdu = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1];
                    await stream.ReadExactlyAsync(pdu, _stop.Token);
                    Interlocked.Increment(ref Requests);
                    var answer = Answer(pdu);
                    var frame = new byte[7 + answer.Length];
                    header.AsSpan(0, 4).CopyTo(frame);
                    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(answer.Length + 1));
                    frame[6] = header[6];
                    answer.CopyTo(frame, 7);
                    await stream.WriteAsync(frame, _stop.Token);
                }
            }
            catch (Exception)
            {
                // el cliente se fue
            }
        }
    }

    private byte[] Answer(byte[] pdu)
    {
        byte[] Error(byte code) => [(byte)(pdu[0] | 0x80), code];
        var address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
        switch (pdu[0])
        {
            case 0x03:
            {
                var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                var answer = new byte[2 + count * 2];
                answer[0] = 0x03;
                answer[1] = (byte)(count * 2);
                for (var i = 0; i < count; i++)
                {
                    if (!Registers.TryGetValue((ushort)(address + i), out var value)) return Error(2);
                    BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(2 + i * 2), value);
                }

                return answer;
            }
            case 0x06:
                if (!Registers.ContainsKey(address)) return Error(2);
                Registers[address] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                return pdu;
            case 0x10:
            {
                var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                for (var i = 0; i < count; i++)
                    if (!Registers.ContainsKey((ushort)(address + i))) return Error(2);
                for (var i = 0; i < count; i++) Registers[(ushort)(address + i)] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(6 + i * 2));
                return pdu[..5];
            }
            default:
                return Error(1);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _accept; } catch { /* parado */ }
        _stop.Dispose();
    }
}
