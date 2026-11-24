// YARG Resurrected - small QR code encoder for the "join by scanning" codes.
// Byte mode, error correction level L, versions 1-5 (up to 106 bytes), mask 0.
// Plain .NET; checked against a real QR decoder (see tests/RemoteServerTests).
using System;
using System.Collections.Generic;

namespace YARG.Remote
{
    public static class RemoteQr
    {
        public const int MAX_BYTES = 106;

        private static readonly int[] _dataCodewords = { 0, 19, 34, 55, 80, 108 };
        private static readonly int[] _eccCodewords = { 0, 7, 10, 15, 20, 26 };

        /// <summary>
        /// Returns the code as [y, x] modules (true = dark), or null if the data is too long.
        /// </summary>
        public static bool[,] Encode(byte[] data)
        {
            int version = 0;
            for (int v = 1; v <= 5; v++)
            {
                if (data.Length <= _dataCodewords[v] - 2)
                {
                    version = v;
                    break;
                }
            }
            if (version == 0)
            {
                return null;
            }

            int size = version * 4 + 17;
            int capacityBits = _dataCodewords[version] * 8;

            // Data bits: mode, length, bytes, terminator, padding
            var bits = new List<bool>(capacityBits);
            AppendBits(bits, 4, 4);
            AppendBits(bits, data.Length, 8);
            foreach (byte b in data)
            {
                AppendBits(bits, b, 8);
            }
            AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));
            AppendBits(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
            {
                AppendBits(bits, pad, 8);
            }

            int dataLength = bits.Count / 8;
            int eccLength = _eccCodewords[version];
            var codewords = new byte[dataLength + eccLength];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                {
                    codewords[i >> 3] |= (byte) (1 << (7 - (i & 7)));
                }
            }
            var ecc = ReedSolomonRemainder(codewords, dataLength, eccLength);
            Array.Copy(ecc, 0, codewords, dataLength, eccLength);

            var modules = new bool[size, size];
            var function = new bool[size, size];

            // Timing patterns
            for (int i = 0; i < size; i++)
            {
                SetFunction(modules, function, 6, i, i % 2 == 0);
                SetFunction(modules, function, i, 6, i % 2 == 0);
            }

            // Finder patterns with separators
            DrawFinder(modules, function, size, 3, 3);
            DrawFinder(modules, function, size, size - 4, 3);
            DrawFinder(modules, function, size, 3, size - 4);

            // Alignment pattern (versions 2-5 have one)
            if (version >= 2)
            {
                int c = size - 7;
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        SetFunction(modules, function, c + dx, c + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
                }
            }

            // Format bits: level L (01), mask 0
            int format = 1 << 3;
            int rem = format;
            for (int i = 0; i < 10; i++)
            {
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            }
            int formatBits = ((format << 10) | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++)
            {
                SetFunction(modules, function, 8, i, Bit(formatBits, i));
            }
            SetFunction(modules, function, 8, 7, Bit(formatBits, 6));
            SetFunction(modules, function, 8, 8, Bit(formatBits, 7));
            SetFunction(modules, function, 7, 8, Bit(formatBits, 8));
            for (int i = 9; i < 15; i++)
            {
                SetFunction(modules, function, 14 - i, 8, Bit(formatBits, i));
            }
            for (int i = 0; i < 8; i++)
            {
                SetFunction(modules, function, size - 1 - i, 8, Bit(formatBits, i));
            }
            for (int i = 8; i < 15; i++)
            {
                SetFunction(modules, function, 8, size - 15 + i, Bit(formatBits, i));
            }
            SetFunction(modules, function, 8, size - 8, true);

            // Data, in the zigzag order, with mask 0 applied
            int bitIndex = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5;
                }

                for (int vert = 0; vert < size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (function[y, x])
                        {
                            continue;
                        }

                        bool dark = bitIndex < codewords.Length * 8 &&
                            ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                        bitIndex++;
                        if ((x + y) % 2 == 0)
                        {
                            dark = !dark;
                        }
                        modules[y, x] = dark;
                    }
                }
            }

            return modules;
        }

        private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

        private static void AppendBits(List<bool> bits, int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) != 0);
            }
        }

        private static void SetFunction(bool[,] modules, bool[,] function, int x, int y, bool dark)
        {
            modules[y, x] = dark;
            function[y, x] = true;
        }

        private static void DrawFinder(bool[,] modules, bool[,] function, int size, int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x < 0 || x >= size || y < 0 || y >= size)
                    {
                        continue;
                    }

                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunction(modules, function, x, y, dist != 2 && dist != 4);
                }
            }
        }

        private static byte[] ReedSolomonRemainder(byte[] data, int dataLength, int degree)
        {
            // Generator polynomial
            var generator = new byte[degree];
            generator[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    generator[j] = Multiply(generator[j], root);
                    if (j + 1 < degree)
                    {
                        generator[j] ^= generator[j + 1];
                    }
                }
                root = Multiply(root, 2);
            }

            var result = new byte[degree];
            for (int k = 0; k < dataLength; k++)
            {
                int factor = data[k] ^ result[0];
                Array.Copy(result, 1, result, 0, degree - 1);
                result[degree - 1] = 0;
                for (int i = 0; i < degree; i++)
                {
                    result[i] ^= Multiply(generator[i], factor);
                }
            }
            return result;
        }

        // Multiplication in GF(2^8) with the QR polynomial 0x11D
        private static byte Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte) z;
        }
    }
}
