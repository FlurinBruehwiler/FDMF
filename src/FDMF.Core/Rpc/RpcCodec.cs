using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;

namespace FDMF.Core.Rpc;

public readonly record struct RpcDecodedMessage(
    MessageType Type,
    Guid RequestId,
    string? MethodName,
    ReadOnlyMemory<byte>[]? ArgPayloads,
    ReadOnlyMemory<byte> Payload,
    int? HelloVersion);

public static class RpcCodec
{
    public static MemoryPackSerializerOptions SerializerOptions = MemoryPackSerializerOptions.Default with
    {
        ServiceProvider = new ServiceProvider()
    };

    public static byte[] EncodeHello(int version)
    {
        var buf = new byte[1 + 4];
        buf[0] = (byte)MessageType.Hello;
        MemoryMarshal.Write(buf.AsSpan(1, 4), version);
        return buf;
    }

    public static byte[] EncodeRequest(MessageType type, Guid requestId, string methodName, object[] parameters)
    {
        if (type != MessageType.Request && type != MessageType.Notification)
            throw new ArgumentOutOfRangeException(nameof(type));

        var memStream = new MemoryStream();
        using var writer = new BinaryWriter(memStream, Encoding.Unicode, true);

        writer.Write((byte)type);
        Span<byte> idBuf = stackalloc byte[16];
        MemoryMarshal.Write(idBuf, requestId);
        writer.Write(idBuf);

        // Method name is UTF-16 chars (no BOM), length in bytes.
        writer.Write(methodName.Length * 2);
        writer.Write(methodName.AsSpan());

        if (parameters.Length > byte.MaxValue)
            throw new InvalidOperationException("Too many parameters");

        writer.Write((byte)parameters.Length);

        foreach (var parameter in parameters)
        {
            var data = MemoryPackSerializer.Serialize(parameter.GetType(), parameter, SerializerOptions);
            writer.Write(data.Length);
            writer.Write(data);
        }

        return memStream.ToArray();
    }

    public static byte[] EncodeResponse(Guid requestId, object response)
    {
        var res = MemoryPackSerializer.Serialize(response.GetType(), response, SerializerOptions);
        var responseBuf = new byte[1 + 16 + res.Length];
        responseBuf[0] = (byte)MessageType.Response;
        MemoryMarshal.Write(responseBuf.AsSpan(1, 16), requestId);
        res.AsSpan().CopyTo(responseBuf.AsSpan(17));
        return responseBuf;
    }

    public static bool TryDecode(ReadOnlyMemory<byte> frame, out RpcDecodedMessage msg)
    {
        msg = default;
        var span = frame.Span;
        if (span.Length < 1)
            return false;

        if ((MessageType)span[0] == MessageType.Hello)
        {
            if (span.Length < 1 + 4)
                return false;

            var ver = MemoryMarshal.Read<int>(span.Slice(1, 4));
            msg = new RpcDecodedMessage(MessageType.Hello, Guid.Empty, null, null, ReadOnlyMemory<byte>.Empty, ver);
            return true;
        }

        if (span.Length < 1 + 16)
            return false;

        var reader = new BinaryReader
        {
            Data = span,
            CurrentOffset = 0,
            HasError = false
        };

        var type = (MessageType)reader.ReadByte();
        var requestId = reader.ReadGuid();

        if (reader.HasError)
            return false;

        if (type == MessageType.Response)
        {
            var payload = frame.Slice(reader.CurrentOffset);
            msg = new RpcDecodedMessage(type, requestId, null, null, payload, null);
            return true;
        }

        if (type == MessageType.Request || type == MessageType.Notification)
        {
            var methodName = new string(reader.ReadUtf16String());
            var argCount = reader.ReadByte();
            if (reader.HasError)
                return false;

            var payloads = new ReadOnlyMemory<byte>[argCount];
            for (int i = 0; i < argCount; i++)
            {
                var len = reader.ReadInt32();
                var slice = reader.ReadSlice(len);
                if (reader.HasError)
                    return false;

                var start = reader.CurrentOffset - len;
                payloads[i] = frame.Slice(start, len);
            }

            msg = new RpcDecodedMessage(type, requestId, methodName, payloads, ReadOnlyMemory<byte>.Empty, null);
            return true;
        }

        // ConnectionClosed or unknown: treat as no-op.
        msg = new RpcDecodedMessage(type, requestId, null, null, ReadOnlyMemory<byte>.Empty, null);
        return true;
    }
}
