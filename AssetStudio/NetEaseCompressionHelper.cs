using System;
using System.IO;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;

namespace AssetStudio
{
    public static class NetEaseCompressionHelper
    {
        public static byte[] DecompressNetEaseVariant(byte[] compressed, int expectedDecompressedSize)
        {
            try
            {
                // Certains fichiers NetEase ont un header custom qu'il faut sauter
                int headerSkip = DetectHeaderSkip(compressed);

                using (var input = new MemoryStream(compressed, headerSkip, compressed.Length - headerSkip))
                using (var lz4Stream = LZ4Stream.Decode(input))
                using (var output = new MemoryStream())
                {
                    lz4Stream.CopyTo(output);
                    var result = output.ToArray();

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

        // Détection du header NetEase pour trouver l'offset réel des données LZ4
        private static int DetectHeaderSkip(byte[] compressed)
        {
            // Dans la majorité des fichiers NetEase, les 4 à 8 premiers octets sont un header
            if (compressed.Length > 8)
            {
                // On vérifie si les 4 premiers octets ressemblent à une taille incohérente
                int potentialSize = BitConverter.ToInt32(compressed, 0);
                if (potentialSize > compressed.Length || potentialSize <= 0)
                {
                    return 8; // On saute les 8 premiers octets
                }
            }

            return 0; // Sinon on ne saute rien
        }
    }
}
