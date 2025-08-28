using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using System;
using System.IO;
using System.Linq;

namespace AssetStudio
{
    [Flags]
    public enum ArchiveFlags
    {
        CompressionTypeMask = 0x3f,
        BlocksAndDirectoryInfoCombined = 0x40,
        BlocksInfoAtTheEnd = 0x80,
        OldWebPluginCompatibility = 0x100,
        BlockInfoNeedPaddingAtStart = 0x200
    }

    [Flags]
    public enum StorageBlockFlags
    {
        CompressionTypeMask = 0x3f,
        Streamed = 0x40
    }

    public enum CompressionType
    {
        None,
        Lzma,
        Lz4,
        Lz4HC,
        Lzham,
        NetEase = 5 // Ajout pour OPFP
    }

    public class BundleFile
    {
        public class Header
        {
            public string signature;
            public uint version;
            public string unityVersion;
            public string unityRevision;
            public long size;
            public uint compressedBlocksInfoSize;
            public uint uncompressedBlocksInfoSize;
            public ArchiveFlags flags;
        }

        public class StorageBlock
        {
            public uint uncompressedSize;
            public uint compressedSize;
            public StorageBlockFlags flags;
        }

        public class Node
        {
            public long offset;
            public long size;
            public uint flags;
            public string path;
        }

        public Header m_Header;
        public StorageBlock[] m_BlocksInfo;
        public Node[] m_DirectoryInfo;
        public StreamFile[] fileList;

        public static BundleFile Create(FileReader reader)
        {
            Console.WriteLine("[Bundle] Création du BundleFile...");
            return reader.Loader is { ReturnsBundleFile: true }
                ? reader.Loader.ProcessBundle(reader)
                : new BundleFile(reader);
        }

        private BundleFile(FileReader reader) : this()
        {
            Console.WriteLine($"[Bundle] Initialisation du bundle : {reader.FullPath}");
            Initialize(reader);

            Console.WriteLine($"[Bundle] Signature détectée : {m_Header.signature} | Version : {m_Header.version}");

            switch (m_Header.signature)
            {
                case "UnityArchive":
                    Console.WriteLine("[Bundle] Format UnityArchive détecté.");
                    break;

                case "UnityWeb":
                case "UnityRaw":
                    Console.WriteLine("[Bundle] Format UnityWeb/UnityRaw détecté.");
                    if (m_Header.version == 6)
                    {
                        goto case "UnityFS";
                    }
                    ReadHeaderAndBlocksInfo(reader);
                    using (var blocksStream = CreateBlocksStream(reader.FullPath))
                    {
                        ReadBlocksAndDirectory(reader, blocksStream);
                        ReadFiles(blocksStream, reader.FullPath);
                    }
                    break;

                case "UnityFS":
                    Console.WriteLine("[Bundle] Format UnityFS détecté.");
                    ReadHeader(reader);
                    ReadBlocksInfoAndDirectory(reader);
                    using (var blocksStream = CreateBlocksStream(reader.FullPath))
                    {
                        ReadBlocks(reader, blocksStream);
                        ReadFiles(blocksStream, reader.FullPath);
                    }
                    break;

                default:
                    Console.WriteLine($"[Bundle] Format inconnu : {m_Header.signature}");
                    break;
            }

            Console.WriteLine("[Bundle] Lecture terminée.");
        }

        public BundleFile()
        {
            m_Header = new Header();
        }

        public void Initialize(EndianBinaryReader reader)
        {
            Console.WriteLine("[Bundle] Lecture de l'en-tête...");
            m_Header.signature = reader.ReadStringToNull();
            m_Header.version = reader.ReadUInt32();
            m_Header.unityVersion = reader.ReadStringToNull();
            m_Header.unityRevision = reader.ReadStringToNull();
            Console.WriteLine($"[Bundle] Header : Signature={m_Header.signature} | Version={m_Header.version} | Unity={m_Header.unityVersion}");
        }

        public void ReadHeaderAndBlocksInfo(EndianBinaryReader reader)
        {
            Console.WriteLine("[Blocks] Lecture du header et des blocks info...");
            if (m_Header.version >= 4)
            {
                var hash = reader.ReadBytes(16);
                var crc = reader.ReadUInt32();
            }
            var minimumStreamedBytes = reader.ReadUInt32();
            m_Header.size = reader.ReadUInt32();
            var numberOfLevelsToDownloadBeforeStreaming = reader.ReadUInt32();
            var levelCount = reader.ReadInt32();
            m_BlocksInfo = new StorageBlock[1];
            for (int i = 0; i < levelCount; i++)
            {
                var storageBlock = new StorageBlock()
                {
                    compressedSize = reader.ReadUInt32(),
                    uncompressedSize = reader.ReadUInt32(),
                };
                if (i == levelCount - 1)
                {
                    m_BlocksInfo[0] = storageBlock;
                }
            }
            if (m_Header.version >= 2)
            {
                var completeFileSize = reader.ReadUInt32();
            }
            if (m_Header.version >= 3)
            {
                var fileInfoHeaderSize = reader.ReadUInt32();
            }
            reader.Position = m_Header.size;
        }

        public Stream CreateBlocksStream(string path)
        {
            Console.WriteLine("[Blocks] Création du flux mémoire pour les blocks...");
            Stream blocksStream;
            var uncompressedSizeSum = m_BlocksInfo.Sum(x => x.uncompressedSize);
            if (uncompressedSizeSum >= int.MaxValue)
            {
                blocksStream = new FileStream(path + ".temp", FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
            }
            else
            {
                blocksStream = new MemoryStream((int)uncompressedSizeSum);
            }
            return blocksStream;
        }

        public void ReadBlocksAndDirectory(EndianBinaryReader reader, Stream blocksStream)
        {
            Console.WriteLine("[Blocks] Lecture des blocks et du répertoire...");
            var isCompressed = m_Header.signature == "UnityWeb";
            foreach (var blockInfo in m_BlocksInfo)
            {
                var uncompressedBytes = reader.ReadBytes((int)blockInfo.compressedSize);
                if (isCompressed)
                {
                    Console.WriteLine("[Blocks] Décompression SevenZip...");
                    using (var memoryStream = new MemoryStream(uncompressedBytes))
                    using (var decompressStream = SevenZipHelper.StreamDecompress(memoryStream))
                    {
                        uncompressedBytes = decompressStream.ToArray();
                    }
                }
                blocksStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
            }
            blocksStream.Position = 0;
            var blocksReader = new EndianBinaryReader(blocksStream);
            var nodesCount = blocksReader.ReadInt32();
            Console.WriteLine($"[Files] Nombre de fichiers dans le bundle : {nodesCount}");
            m_DirectoryInfo = new Node[nodesCount];
            for (int i = 0; i < nodesCount; i++)
            {
                m_DirectoryInfo[i] = new Node
                {
                    path = blocksReader.ReadStringToNull(),
                    offset = blocksReader.ReadUInt32(),
                    size = blocksReader.ReadUInt32()
                };
                Console.WriteLine($"[Files] Fichier #{i + 1}: {m_DirectoryInfo[i].path}");
            }
        }

        public void ReadFiles(Stream blocksStream, string path)
        {
            Console.WriteLine("[Files] Lecture des fichiers Unity...");
            fileList = new StreamFile[m_DirectoryInfo.Length];
            for (int i = 0; i < m_DirectoryInfo.Length; i++)
            {
                var node = m_DirectoryInfo[i];
                var file = new StreamFile();
                fileList[i] = file;
                file.path = node.path;
                file.fileName = Path.GetFileName(node.path);
                Console.WriteLine($"[Files] Extraction du fichier : {file.fileName} ({node.size} octets)");
                if (node.size >= int.MaxValue)
                {
                    var extractPath = path + "_unpacked" + Path.DirectorySeparatorChar;
                    Directory.CreateDirectory(extractPath);
                    file.stream = new FileStream(extractPath + file.fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
                }
                else
                {
                    file.stream = new MemoryStream((int)node.size);
                }
                blocksStream.Position = node.offset;
                blocksStream.CopyTo(file.stream, node.size);
                file.stream.Position = 0;
            }
            Console.WriteLine("[Files] Tous les fichiers Unity ont été lus.");
        }

        public void ReadHeader(EndianBinaryReader reader)
        {
            Console.WriteLine("[Bundle] Lecture du header UnityFS...");
            m_Header.size = reader.ReadInt64();
            m_Header.compressedBlocksInfoSize = reader.ReadUInt32();
            m_Header.uncompressedBlocksInfoSize = reader.ReadUInt32();
            m_Header.flags = (ArchiveFlags)reader.ReadUInt32();
            if (m_Header.signature != "UnityFS")
            {
                reader.ReadByte();
            }
            Console.WriteLine($"[Bundle] Taille : {m_Header.size} | Flags : {m_Header.flags}");
        }

        public void ReadBlocksInfoAndDirectory(EndianBinaryReader reader, Action<byte[]>? preDecompressionCallback = null)
        {
            Console.WriteLine("[Blocks] Lecture des informations sur les blocks...");
            byte[] blocksInfoBytes;
            if (m_Header.version >= 7)
            {
                reader.AlignStream(16);
            }
            else if (m_Header.version == 6)
            {
                var temp = (stackalloc byte[(int)(16 - reader.Position % 16)]);
                reader.CheckedRead(temp);
                if (temp.IndexOfAnyExcept(byte.MinValue) != -1)
                    reader.Position -= temp.Length;
            }
            if ((m_Header.flags & ArchiveFlags.BlocksInfoAtTheEnd) != 0)
            {
                var position = reader.Position;
                reader.Position = reader.BaseStream.Length - m_Header.compressedBlocksInfoSize;
                blocksInfoBytes = reader.ReadBytes((int)m_Header.compressedBlocksInfoSize);
                reader.Position = position;
            }
            else
            {
                blocksInfoBytes = reader.ReadBytes((int)m_Header.compressedBlocksInfoSize);
            }
            MemoryStream blocksInfoUncompresseddStream;
            var uncompressedSize = m_Header.uncompressedBlocksInfoSize;
            var compressionType = (CompressionType)(m_Header.flags & ArchiveFlags.CompressionTypeMask);

            Console.WriteLine($"[Blocks] Méthode de compression : {compressionType}");

            preDecompressionCallback?.Invoke(blocksInfoBytes);

            switch (compressionType)
            {
                case CompressionType.None:
                    Console.WriteLine("[Blocks] Pas de compression.");
                    blocksInfoUncompresseddStream = new MemoryStream(blocksInfoBytes);
                    break;

                case CompressionType.Lzma:
                    Console.WriteLine("[Blocks] Décompression LZMA...");
                    blocksInfoUncompresseddStream = new MemoryStream((int)(uncompressedSize));
                    using (var blocksInfoCompressedStream = new MemoryStream(blocksInfoBytes))
                    {
                        SevenZipHelper.StreamDecompress(blocksInfoCompressedStream, blocksInfoUncompresseddStream, m_Header.compressedBlocksInfoSize, m_Header.uncompressedBlocksInfoSize);
                    }
                    blocksInfoUncompresseddStream.Position = 0;
                    break;

                case CompressionType.Lz4:
                case CompressionType.Lz4HC:
                    Console.WriteLine("[Blocks] Décompression LZ4...");
                    var uncompressedBytes = new byte[uncompressedSize];
                    var numWrite = LZ4Codec.Decode(blocksInfoBytes, uncompressedBytes);
                    if (numWrite != uncompressedSize)
                    {
                        throw new IOException($"[Blocks] Erreur LZ4 : {numWrite} octets écrits au lieu de {uncompressedSize}");
                    }
                    blocksInfoUncompresseddStream = new MemoryStream(uncompressedBytes);
                    break;

                case CompressionType.NetEase:
                    Console.WriteLine("[NetEase] Début de la décompression spéciale...");
                    var neteaseDecompressed = NetEaseCompressionHelper.DecompressNetEaseVariant(blocksInfoBytes, (int)uncompressedSize);
                    if (neteaseDecompressed == null)
                        throw new IOException("[NetEase] Échec de la décompression NetEase !");
                    Console.WriteLine($"[NetEase] Décompression réussie : {neteaseDecompressed.Length} octets.");
                    blocksInfoUncompresseddStream = new MemoryStream(neteaseDecompressed);
                    break;

                default:
                    throw new IOException($"[Blocks] Compression non supportée : {compressionType}");
            }

            using (var blocksInfoReader = new EndianBinaryReader(blocksInfoUncompresseddStream))
            {
                var uncompressedDataHash = blocksInfoReader.ReadBytes(16);
                var blocksInfoCount = blocksInfoReader.ReadInt32();
                Console.WriteLine($"[Blocks] Nombre de blocks : {blocksInfoCount}");
                m_BlocksInfo = new StorageBlock[blocksInfoCount];
                for (int i = 0; i < blocksInfoCount; i++)
                {
                    m_BlocksInfo[i] = new StorageBlock
                    {
                        uncompressedSize = blocksInfoReader.ReadUInt32(),
                        compressedSize = blocksInfoReader.ReadUInt32(),
                        flags = (StorageBlockFlags)blocksInfoReader.ReadUInt16()
                    };
                    Console.WriteLine($"[Blocks] Block #{i + 1} : {m_BlocksInfo[i].compressedSize} → {m_BlocksInfo[i].uncompressedSize}");
                }

                var nodesCount = blocksInfoReader.ReadInt32();
                Console.WriteLine($"[Files] Nombre de fichiers Unity : {nodesCount}");
                m_DirectoryInfo = new Node[nodesCount];
                for (int i = 0; i < nodesCount; i++)
                {
                    m_DirectoryInfo[i] = new Node
                    {
                        offset = blocksInfoReader.ReadInt64(),
                        size = blocksInfoReader.ReadInt64(),
                        flags = blocksInfoReader.ReadUInt32(),
                        path = blocksInfoReader.ReadStringToNull(),
                    };
                    Console.WriteLine($"[Files] Fichier #{i + 1} : {m_DirectoryInfo[i].path}");
                }
            }
            if ((m_Header.flags & ArchiveFlags.BlockInfoNeedPaddingAtStart) != 0)
            {
                reader.AlignStream(16);
            }
        }

        public void ReadBlocks(EndianBinaryReader reader, Stream blocksStream)
        {
            Console.WriteLine("[Blocks] Début de la lecture des blocks...");
            foreach (var blockInfo in m_BlocksInfo)
            {
                var compressionType = (CompressionType)(blockInfo.flags & StorageBlockFlags.CompressionTypeMask);
                Console.WriteLine($"[Blocks] Lecture block : {blockInfo.compressedSize} → {blockInfo.uncompressedSize} | Compression={compressionType}");
                switch (compressionType)
                {
                    case CompressionType.None:
                        reader.BaseStream.CopyTo(blocksStream, blockInfo.compressedSize);
                        break;

                    case CompressionType.Lzma:
                        Console.WriteLine("[Blocks] Décompression LZMA...");
                        SevenZipHelper.StreamDecompress(reader.BaseStream, blocksStream, blockInfo.compressedSize, blockInfo.uncompressedSize);
                        break;

                    case CompressionType.Lz4:
                    case CompressionType.Lz4HC:
                        Console.WriteLine("[Blocks] Décompression LZ4...");
                        var compressedSize = (int)blockInfo.compressedSize;
                        var compressedBytes = BigArrayPool<byte>.Shared.Rent(compressedSize);

                        var uncompressedSize = (int)blockInfo.uncompressedSize;
                        var uncompressedBytes = BigArrayPool<byte>.Shared.Rent(uncompressedSize);

                        reader.CheckedRead(compressedBytes, 0, compressedSize);

                        var numWrite = LZ4Codec.Decode(compressedBytes, 0, compressedSize, uncompressedBytes, 0, uncompressedSize);
                        if (numWrite != uncompressedSize)
                        {
                            throw new IOException($"[Blocks] Erreur LZ4 : {numWrite} octets écrits au lieu de {uncompressedSize}");
                        }

                        blocksStream.Write(uncompressedBytes, 0, uncompressedSize);
                        BigArrayPool<byte>.Shared.Return(compressedBytes);
                        BigArrayPool<byte>.Shared.Return(uncompressedBytes);
                        break;

                    case CompressionType.NetEase:
                        Console.WriteLine("[NetEase] Décompression d'un block...");
                        var compressedSizeNetEase = (int)blockInfo.compressedSize;
                        var compressedBytesNetEase = reader.ReadBytes(compressedSizeNetEase);

                        var neteaseDecompressed = NetEaseCompressionHelper.DecompressNetEaseVariant(compressedBytesNetEase, (int)blockInfo.uncompressedSize);
                        if (neteaseDecompressed == null)
                            throw new IOException("[NetEase] Échec de la décompression NetEase !");

                        Console.WriteLine($"[NetEase] Block décompressé : {neteaseDecompressed.Length} octets.");
                        blocksStream.Write(neteaseDecompressed, 0, neteaseDecompressed.Length);
                        break;

                    default:
                        throw new IOException($"[Blocks] Compression non supportée : {compressionType}");
                }
            }
            blocksStream.Position = 0;
            Console.WriteLine("[Blocks] Tous les blocks ont été traités.");
        }
    }
}
