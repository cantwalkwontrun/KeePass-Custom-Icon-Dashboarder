using System;
using System.IO;

namespace CustomIconDashboarderPlugin
{
    internal static class IconImageLimits
    {
        private static uint BigEndian(byte[] data, int offset)
        { return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3]; }

        private static void Dimensions(long width, long height)
        {
            if (width <= 0 || height <= 0 || width > 2048 || height > 2048 || width * height > 4 * 1024 * 1024)
                throw new InvalidDataException("Unsupported icon dimensions.");
        }

        private static bool Png(byte[] data, int start, int length)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (length < 24) return false;
            for (int i = 0; i < signature.Length; i++) if (data[start + i] != signature[i]) return false;
            return BigEndian(data, start + 8) == 13 && data[start + 12] == 'I' && data[start + 13] == 'H' && data[start + 14] == 'D' && data[start + 15] == 'R';
        }

        private static bool DibSize(uint size) { return size == 40 || size == 52 || size == 56 || size == 108 || size == 124; }

        internal static void Validate(byte[] data)
        {
            if (data == null || data.Length < 10) throw new InvalidDataException("Invalid icon image.");
            if (Png(data, 0, data.Length))
            { Dimensions(BigEndian(data, 16), BigEndian(data, 20)); return; }
            if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            { Dimensions(BitConverter.ToUInt16(data, 6), BitConverter.ToUInt16(data, 8)); return; }
            if (data.Length >= 26 && data[0] == 'B' && data[1] == 'M')
            {
                int dib = BitConverter.ToInt32(data, 14);
                if (dib == 12) Dimensions(BitConverter.ToUInt16(data, 18), BitConverter.ToUInt16(data, 20));
                else if (DibSize((uint)dib) && data.Length >= 14 + dib)
                {
                    int compression = BitConverter.ToInt32(data, 30);
                    if (compression == 4 || compression == 5) throw new InvalidDataException("Nested bitmap image formats are unsupported.");
                    Dimensions(BitConverter.ToInt32(data, 18), Math.Abs((long)BitConverter.ToInt32(data, 22)));
                }
                else throw new InvalidDataException("Unsupported bitmap header.");
                return;
            }
            if (BitConverter.ToUInt16(data, 0) == 0 && BitConverter.ToUInt16(data, 2) == 1)
            {
                int count = BitConverter.ToUInt16(data, 4);
                if (count == 0 || count > 128 || data.Length < 6 + count * 16) throw new InvalidDataException("Invalid ICO directory.");
                long totalPixels = 0;
                var ranges = new System.Collections.Generic.List<Tuple<uint, uint>>();
                for (int i = 0; i < count; i++)
                {
                    int entry = 6 + i * 16;
                    uint length = BitConverter.ToUInt32(data, entry + 8), offset = BitConverter.ToUInt32(data, entry + 12);
                    if (length < 16 || offset < 6 + count * 16 || (ulong)offset + length > (ulong)data.Length) throw new InvalidDataException("Invalid ICO payload.");
                    uint end = offset + length;
                    foreach (var range in ranges)
                        if (offset < range.Item2 && end > range.Item1) throw new InvalidDataException("Overlapping ICO payloads are unsupported.");
                    ranges.Add(Tuple.Create(offset, end));
                    int start = (int)offset;
                    long width, height;
                    if (Png(data, start, (int)length))
                    { width = BigEndian(data, start + 16); height = BigEndian(data, start + 20); }
                    else if (length >= 40 && DibSize(BitConverter.ToUInt32(data, start)) && length >= BitConverter.ToUInt32(data, start))
                    {
                        int compression = BitConverter.ToInt32(data, start + 16);
                        if (compression == 4 || compression == 5) throw new InvalidDataException("Nested ICO image formats are unsupported.");
                        width = BitConverter.ToInt32(data, start + 4);
                        height = Math.Abs((long)BitConverter.ToInt32(data, start + 8)) / 2;
                    }
                    else throw new InvalidDataException("Unsupported ICO payload.");
                    Dimensions(width, height);
                    totalPixels += width * height;
                    if (totalPixels > 4 * 1024 * 1024) throw new InvalidDataException("ICO contains too many decoded pixels.");
                }
                return;
            }
            if (data[0] == 255 && data[1] == 216)
            {
                int position = 2;
                while (position + 4 <= data.Length)
                {
                    if (data[position++] != 255) throw new InvalidDataException("Invalid JPEG marker.");
                    while (position < data.Length && data[position] == 255) position++;
                    if (position >= data.Length) break;
                    int marker = data[position++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || (marker >= 208 && marker <= 215)) continue;
                    if (position + 2 > data.Length) break;
                    int length = (data[position] << 8) | data[position + 1];
                    if (length < 2 || position + length > data.Length) break;
                    if (marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204)
                    {
                        if (length < 8) break;
                        Dimensions((data[position + 5] << 8) | data[position + 6], (data[position + 3] << 8) | data[position + 4]);
                        return;
                    }
                    position += length;
                }
            }
            throw new InvalidDataException("Unsupported or malformed icon format.");
        }
    }
}
