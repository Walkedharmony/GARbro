using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Compression;
using GameRes.Formats.Properties;
using GameRes.Utility;

namespace GameRes.Formats.ZeroNovel
{
    [Export(typeof(ArchiveFormat))]
    public class ArchFormat : ArchiveFormat
    {
        public override string Tag { get { return "ARCH"; } }
        public override string Description { get { return "ZeroNovel ARCH archive"; } }
        public override uint Signature { get { return 0x48435241; } } 

        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        private static readonly Dictionary<string, string> FileTypeMap = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            { ".ini", "script" },
            { ".cfg", "config" },
            { ".txt", "text" },
            { ".xml", "text" },
            { ".json", "text" },
            { ".csv", "text" },
            { ".png", "image" },
            { ".jpg", "image" },
            { ".jpeg", "image" },
            { ".bmp", "image" },
            { ".tga", "image" },
            { ".gif", "image" },
            { ".wmv", "video" },
            { ".mp4", "video" },
            { ".avi", "video" },
            { ".mov", "video" },
            { ".mp3", "audio" },
            { ".wav", "audio" },
            { ".ogg", "audio" },
            { ".scn", "scene" },
            { ".dat", "data" },
            { ".bin", "data" },
            { ".lua", "script" },
            { ".py", "script" },
            { ".dll", "binary" },
            { ".exe", "binary" }
        };

        public ArchFormat()
        {
            Extensions = new string[] { "arch" };
        }

        private string GetFileType(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
                return "file";

            if (FileTypeMap.TryGetValue(extension, out string fileType))
                return fileType;

            return "file";
        }

        public override ArcFile TryOpen(ArcView file)
        {
            
            var header = new ArchHeader();
            using (var header_stream = file.CreateStream(0, 64)) 
            {
                if (header_stream.Length < 64)
                    return null;

                header.magic = header_stream.ReadUInt32();
                header.version = header_stream.ReadUInt32();
                header.fileCount = header_stream.ReadUInt32();
                header.indexOffset = header_stream.ReadUInt32();
                header.flags = header_stream.ReadUInt32();
                header.reserved = header_stream.ReadBytes(44);
            }

            
            if (header.magic != Signature || header.fileCount == 0)
                return null;

            
            uint index_offset = header.indexOffset;
            uint index_size = header.fileCount * 292; 

            if (index_offset <= 0 || index_offset + (long)index_size > file.MaxOffset)
                return null;

            var dir = new List<Entry>((int)header.fileCount);
            using (var index = file.CreateStream(index_offset, index_size))
            {
                var buffer = new byte[292]; 
                for (uint i = 0; i < header.fileCount; ++i)
                {
                    if (index.Read(buffer, 0, buffer.Length) != buffer.Length)
                        return null;

                    
                    uint offset = BitConverter.ToUInt32(buffer, 260);
                    uint size = BitConverter.ToUInt32(buffer, 264);
                    uint compressedSize = BitConverter.ToUInt32(buffer, 268);
                    byte compressionType = buffer[288]; 

                    if (size == 0)
                        continue;

                    
                    int name_length = Array.IndexOf(buffer, (byte)0, 0, 260);
                    if (name_length < 0) name_length = 260;
                    string name = Encoding.UTF8.GetString(buffer, 0, name_length);

                    var packed_entry = new PackedEntry
                    {
                        Name = name,
                        Offset = offset,
                        Size = compressionType == 1 ? compressedSize : size,
                        UnpackedSize = size,
                        IsPacked = compressionType == 1,
                        Type = GetFileType(name) 
                    };

                    if (!packed_entry.CheckPlacement(file.MaxOffset))
                        return null;

                    dir.Add(packed_entry);
                }
            }

            return new ArcFile(file, this, dir);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (pent == null || !pent.IsPacked)
                return arc.File.CreateStream(entry.Offset, entry.Size);

            
            var input = arc.File.CreateStream(entry.Offset, entry.Size);
            return new ZLibStream(input, CompressionMode.Decompress);
        }

        private struct ArchHeader
        {
            public uint magic;
            public uint version;
            public uint fileCount;
            public uint indexOffset;
            public uint flags;
            public byte[] reserved;
        }
    }
}