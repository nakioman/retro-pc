using System.Text;

namespace RetroBox.Core;

public static class RetroBoxFloppyId
{
    public const int NfcPayloadMaxBytes = 32;

    private const string Base32Alphabet = "0123456789abcdefghijklmnopqrstuv";

    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(bytes);

        var builder = new StringBuilder(26);
        var buffer = 0;
        var bits = 0;

        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                bits -= 5;
                builder.Append(Base32Alphabet[(buffer >> bits) & 31]);
            }
        }

        if (bits > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return builder.ToString();
    }

    public static bool FitsNfcPayload(string id, string mode)
    {
        return Encoding.UTF8.GetByteCount(id) + 1 + Encoding.UTF8.GetByteCount(mode) <= NfcPayloadMaxBytes;
    }
}
