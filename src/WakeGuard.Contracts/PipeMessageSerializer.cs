using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WakeGuard.Contracts;

public static class PipeMessageSerializer
{
    private const int HeaderSize = sizeof(int);

    public static async ValueTask WriteAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, GetTypeInfo<T>());
        if (payload.Length is 0 or > ProtocolConstants.MaximumMessageBytes)
        {
            throw new InvalidDataException($"IPC message size {payload.Length} is outside the allowed range.");
        }

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength is <= 0 or > ProtocolConstants.MaximumMessageBytes)
        {
            throw new InvalidDataException($"IPC message length {payloadLength} is invalid.");
        }

        var payload = new byte[payloadLength];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(payload, GetTypeInfo<T>())
            ?? throw new InvalidDataException("IPC message contained JSON null.");
    }

    private static JsonTypeInfo<T> GetTypeInfo<T>() => typeof(T) switch
    {
        var type when type == typeof(ServiceRequest) =>
            (JsonTypeInfo<T>)(object)WakeGuardJsonContext.Default.ServiceRequest,
        var type when type == typeof(ServiceResponse) =>
            (JsonTypeInfo<T>)(object)WakeGuardJsonContext.Default.ServiceResponse,
        _ => throw new NotSupportedException($"IPC message type {typeof(T).FullName} is not registered."),
    };
}
