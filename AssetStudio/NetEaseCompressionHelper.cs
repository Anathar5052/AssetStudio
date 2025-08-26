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
                // On prépare le buffer de sortie
                byte[] uncompressed = new byte[expectedDecompressedSize];

                // Décompression via LZ4 classique (block)
                int decoded = LZ4Codec.Decode(
                    compressed,              // données compressées
                    0,                       // offset dans compressed
                    compressed.Length,       // taille totale compressée
                    uncompressed,            // buffer de sortie
                    0,                       // offset dans uncompressed
                    expectedDecompressedSize // taille attendue décompressée
                );

                // Vérification de la taille décompressée
                if (decoded != expectedDecompressedSize)
                {
                    Console.WriteLine($"[NetEase] Avertissement : attendu {expectedDecompressedSize} octets, obtenu {decoded} octets.");
                }
                else
                {
                    Console.WriteLine($"[NetEase] Décompression OK : {decoded} octets.");
                }

                return uncompressed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetEase] Erreur de décompression : {ex.Message}");
                return null;
            }
        }
    }
}
