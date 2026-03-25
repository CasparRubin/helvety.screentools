using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

namespace helvety.screentools.Editor
{
    internal static class PngEditableMetadataCodec
    {
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private const string MetadataChunkType = "heDS";
        private const int ChunkOverheadBytes = 12;
        private const int MaxPayloadBytes = 1024 * 1024;

        internal static bool TryReadEditableState(byte[] pngBytes, out string payloadJson)
        {
            payloadJson = string.Empty;
            if (!HasPngSignature(pngBytes))
            {
                return false;
            }

            var offset = PngSignature.Length;
            while (offset + 8 <= pngBytes.Length)
            {
                if (!TryReadChunkHeader(pngBytes, offset, out var dataLength, out var chunkType))
                {
                    return false;
                }

                var chunkTotalLength = checked(ChunkOverheadBytes + dataLength);
                if (offset + chunkTotalLength > pngBytes.Length)
                {
                    return false;
                }

                if (string.Equals(chunkType, MetadataChunkType, StringComparison.Ordinal))
                {
                    var dataOffset = offset + 8;
                    var rawPayload = new byte[dataLength];
                    Buffer.BlockCopy(pngBytes, dataOffset, rawPayload, 0, dataLength);
                    if (TryReadPayloadUtf8(rawPayload, out payloadJson))
                    {
                        return true;
                    }

                    return false;
                }

                offset += chunkTotalLength;
                if (string.Equals(chunkType, "IEND", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return false;
        }

        internal static byte[] WriteWithEditableState(byte[] pngBytes, string payloadJson)
        {
            if (!HasPngSignature(pngBytes))
            {
                throw new InvalidDataException("Input is not a PNG stream.");
            }

            var utf8 = Encoding.UTF8.GetBytes(payloadJson ?? string.Empty);
            if (utf8.Length > MaxPayloadBytes)
            {
                throw new InvalidDataException("Editable metadata payload is too large.");
            }

            var metadataChunk = BuildChunk(MetadataChunkType, utf8);

            using var output = new MemoryStream(pngBytes.Length + metadataChunk.Length + 64);
            output.Write(PngSignature, 0, PngSignature.Length);

            var offset = PngSignature.Length;
            var inserted = false;
            while (offset + 8 <= pngBytes.Length)
            {
                if (!TryReadChunkHeader(pngBytes, offset, out var dataLength, out var chunkType))
                {
                    throw new InvalidDataException("PNG stream has invalid chunk header.");
                }

                var chunkTotalLength = checked(ChunkOverheadBytes + dataLength);
                if (offset + chunkTotalLength > pngBytes.Length)
                {
                    throw new InvalidDataException("PNG stream has a truncated chunk.");
                }

                if (!string.Equals(chunkType, MetadataChunkType, StringComparison.Ordinal))
                {
                    if (!inserted && string.Equals(chunkType, "IEND", StringComparison.Ordinal))
                    {
                        output.Write(metadataChunk, 0, metadataChunk.Length);
                        inserted = true;
                    }

                    output.Write(pngBytes, offset, chunkTotalLength);
                }

                offset += chunkTotalLength;
            }

            if (!inserted)
            {
                output.Write(metadataChunk, 0, metadataChunk.Length);
            }

            return output.ToArray();
        }

        private static bool HasPngSignature(byte[] bytes)
        {
            return bytes is { Length: >= 8 } && PngSignature.SequenceEqual(bytes.Take(PngSignature.Length));
        }

        private static bool TryReadChunkHeader(byte[] pngBytes, int offset, out int dataLength, out string chunkType)
        {
            dataLength = 0;
            chunkType = string.Empty;
            if (offset + 8 > pngBytes.Length)
            {
                return false;
            }

            dataLength = BinaryPrimitives.ReadInt32BigEndian(pngBytes.AsSpan(offset, 4));
            if (dataLength < 0)
            {
                return false;
            }

            chunkType = Encoding.ASCII.GetString(pngBytes, offset + 4, 4);
            return true;
        }

        private static byte[] BuildChunk(string chunkType, byte[] data)
        {
            var chunkTypeBytes = Encoding.ASCII.GetBytes(chunkType);
            var result = new byte[ChunkOverheadBytes + data.Length];
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(0, 4), data.Length);
            Buffer.BlockCopy(chunkTypeBytes, 0, result, 4, 4);
            Buffer.BlockCopy(data, 0, result, 8, data.Length);

            var crcInput = new byte[chunkTypeBytes.Length + data.Length];
            Buffer.BlockCopy(chunkTypeBytes, 0, crcInput, 0, chunkTypeBytes.Length);
            Buffer.BlockCopy(data, 0, crcInput, chunkTypeBytes.Length, data.Length);
            var crc = ComputeCrc32(crcInput);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8 + data.Length, 4), crc);
            return result;
        }

        private static bool TryReadPayloadUtf8(byte[] payloadBytes, out string text)
        {
            text = string.Empty;
            if (payloadBytes.Length == 0)
            {
                return false;
            }

            try
            {
                text = Encoding.UTF8.GetString(payloadBytes);
                if (LooksLikeJson(text))
                {
                    return true;
                }
            }
            catch
            {
                // Try legacy compressed payload below.
            }

            try
            {
                using var input = new MemoryStream(payloadBytes);
                using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8);
                text = reader.ReadToEnd();
                return LooksLikeJson(text);
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            return trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal);
        }

        private static uint ComputeCrc32(byte[] data)
        {
            const uint polynomial = 0xEDB88320u;
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                var current = (crc ^ b) & 0xFFu;
                for (var i = 0; i < 8; i++)
                {
                    current = (current & 1u) != 0 ? (current >> 1) ^ polynomial : current >> 1;
                }

                crc = (crc >> 8) ^ current;
            }

            return ~crc;
        }
    }
}
