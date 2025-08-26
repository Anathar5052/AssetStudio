using System;
using System.IO;
using K4os.Compression.LZ4.Streams;

namespace AssetStudio
{
    public static class NetEaseCompressionHelper
    {
        public static byte[] DecompressNetEaseVariant(byte[] compressed, int expectedDecompressedSize)
        {
            try
            {
                using (var input = new MemoryStream(compressed))
                using (var lz4Stream = LZ4Stream.Decode(input))
                using (var output = new MemoryStream())
                {
                    lz4Stream.CopyTo(output);

                    byte[] result = output.ToArray();

                    // Vérification : taille correcte ?
                    if (result.Length != expectedDecompressedSize)
                    {
                        Console.WriteLine($"[NetEase] Avertissement : attendu {expectedDecompressedSize} octets, obtenu {result.Length} octets.");
                    }
                    else
                    {
                        Console.WriteLine($"[NetEase] Décompression OK : {result.Length} octets.");
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetEase] Erreur de décompression : {ex.Message}");
                return null;
            }
        }
    }
}
