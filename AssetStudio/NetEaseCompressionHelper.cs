using System;
using K4os.Compression.LZ4;

namespace AssetStudio
{
    public static class NetEaseCompressionHelper
    {
        public static byte[] DecompressNetEaseVariant(byte[] compressed, int expectedDecompressedSize)
        {
            // On alloue le buffer de sortie
            byte[] output = new byte[expectedDecompressedSize];

            // Tentative 1 : décompression directe
            int decoded = TryDecode(compressed, 0, compressed.Length, output, expectedDecompressedSize);
            if (decoded == expectedDecompressedSize)
                return output;

            // Tentative 2 : certains fichiers NetEase ont des en-têtes personnalisés
            for (int skip = 1; skip <= 64 && skip < compressed.Length; skip++)
            {
                decoded = TryDecode(compressed, skip, compressed.Length - skip, output, expectedDecompressedSize);
                if (decoded == expectedDecompressedSize)
                    return output;
            }

            // Si aucune tentative ne marche, on signale une erreur
            throw new InvalidOperationException(
                $"Échec de la décompression NetEase : attendu {expectedDecompressedSize} octets mais obtenu {decoded}."
            );
        }

        private static int TryDecode(byte[] compressed, int offset, int length, byte[] output, int expected)
        {
            try
            {
                return LZ4Codec.Decode(
                    compressed, offset, length,
                    output, 0, expected
                );
            }
            catch
            {
                return -1;
            }
        }
    }
}
