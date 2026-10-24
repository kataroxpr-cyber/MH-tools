using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace ClassicMhTools.Core.CustomQuests;

public sealed record DecodedQuestContainer(byte[] Payload, uint Header, byte[] Footer);

public static class QuestCrypto
{
    public const int ContainerFileSize = 0x50400;
    public const int PayloadSize = ContainerFileSize - 0x100 - 8;
    private const string ExtdataKey = "blowfish key iorajegqmrna4itjeangmb agmwgtobjteowhv9mope";
    private const string DlcKey = "AgK2DYheaCjyHGPB";

    // MH4U swaps each 32-bit word around standard Blowfish ECB (not CBC).
    private static byte[] Transform(ReadOnlySpan<byte> input, string key, bool encrypt)
    {
        if (input.Length % 8 != 0) throw new InvalidDataException("Invalid Blowfish block length.");
        var data = input.ToArray();
        SwapWords(data);
        var engine = new BlowfishEngine();
        engine.Init(encrypt, new KeyParameter(Encoding.ASCII.GetBytes(key)));
        for (var i = 0; i < data.Length; i += 8) engine.ProcessBlock(data, i, data, i);
        SwapWords(data);
        return data;
    }

    private static void SwapWords(byte[] data)
    {
        for (var i = 0; i < data.Length; i += 4) data.AsSpan(i, 4).Reverse();
    }

    private static void ApplyRollingXor(Span<byte> data, uint seed)
    {
        for (var i = 0; i < data.Length; i += 2)
        {
            seed = (seed == 0 ? 1 : seed) * 0xB0 % 0xFF53;
            BinaryPrimitives.WriteUInt16LittleEndian(data[i..],
                (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(data[i..]) ^ seed));
        }
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        foreach (var value in data) sum = unchecked(sum + value);
        return sum;
    }

    public static DecodedQuestContainer DecodeContainer(ReadOnlySpan<byte> file)
    {
        if (file.Length != ContainerFileSize) throw new InvalidDataException("Expected an MH4U quest1-quest5 container (0x50400 bytes).");
        var plain = Transform(file[..^0x100], ExtdataKey, false);
        var header = BinaryPrimitives.ReadUInt32LittleEndian(plain);
        if ((header & 0xFFFF) != 0x10) throw new InvalidDataException("Unsupported quest encryption header.");
        ApplyRollingXor(plain.AsSpan(4), header >> 16);
        var payload = plain.AsSpan(8);
        if (BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(4)) != Checksum(payload))
            throw new InvalidDataException("Quest container checksum failed. No changes were made.");
        return new(payload.ToArray(), header, file[^0x100..].ToArray());
    }

    public static byte[] EncodeContainer(DecodedQuestContainer container)
    {
        if (container.Payload.Length != PayloadSize || container.Footer.Length != 0x100 || (container.Header & 0xFFFF) != 0x10)
            throw new InvalidDataException("Invalid quest container dimensions/header.");
        var plain = new byte[PayloadSize + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(plain, container.Header);
        BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(4), Checksum(container.Payload));
        container.Payload.CopyTo(plain, 8);
        ApplyRollingXor(plain.AsSpan(4), container.Header >> 16);
        var result = new byte[ContainerFileSize];
        Transform(plain, ExtdataKey, true).CopyTo(result, 0);
        container.Footer.CopyTo(result, result.Length - 0x100);
        return result;
    }

    public static byte[] DecodeMib(ReadOnlySpan<byte> file)
    {
        if (file.Length >= 8 && file.Slice(4, 4).SequenceEqual("v005"u8)) return file.ToArray();
        if (file.Length < 28 || file.Length > 0x10004 || (file.Length - 4) % 8 != 0)
            throw new InvalidDataException("Expected a raw v005 or US/EU encrypted .mib file.");
        var size = BinaryPrimitives.ReadUInt32BigEndian(file[^4..]);
        if (size < 20 || size > file.Length - 4) throw new InvalidDataException("Invalid MIB size footer.");
        var plain = Transform(file[..^4], DlcKey, false);
        var data = plain.AsSpan(0, (int)size - 20);
        if (!CryptographicOperations.FixedTimeEquals(SHA1.HashData(data), plain.AsSpan((int)size - 20, 20)))
            throw new InvalidDataException("MIB SHA-1 failed (corrupt file or unsupported region).");
        return data.ToArray();
    }

    public static byte[] EncodeMib(ReadOnlySpan<byte> raw)
    {
        _ = new QuestDocument(raw.ToArray());
        var size = raw.Length + 20;
        var plain = new byte[(size + 7) / 8 * 8];
        raw.CopyTo(plain);
        SHA1.HashData(raw).CopyTo(plain, raw.Length);
        var file = new byte[plain.Length + 4];
        Transform(plain, DlcKey, true).CopyTo(file, 0);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(file.Length - 4), (uint)size);
        return file;
    }
}
