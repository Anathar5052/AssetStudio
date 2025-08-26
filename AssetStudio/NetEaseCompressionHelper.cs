using System;
using K4os.Compression.LZ4;

namespace AssetStudio
{
    public static class NetEaseCompressionHelper
    {
        public static byte[] DecompressNetEaseVariant(byte[] compressed, int expectedDecompressedSize)
        {
            try
            {
                byte[] output = new byte[expectedDecompressedSize];

                // Décompression via LZ4 classique
                int decoded = LZ4Codec.Decode(compressed, 0, compressed.Length, output, 0, expectedDecompressedSize);
                if (decoded == expectedDecompressedSize)
                    return output;

                // Si ça échoue, on tente de sauter des headers éventuels
                for (int skip = 1; skip <= 64 && skip < compressed.Length; skip++)
                {
                    try
                    {
                        decoded = LZ4Codec.Decode(compressed, skip, compressed.Length - skip, output, 0, expectedDecompressedSize);
                        if (decoded == expectedDecompressedSize)
                            return output;
                    }
                    catch
                    {
                        // On continue d'essayer
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
