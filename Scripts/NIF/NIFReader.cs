using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace OpenFo3.NIF
{
    public class NIFReader
    {
        public List<NIFBlock> Blocks = new List<NIFBlock>();
        public List<int> RootBlockIndices = new List<int>();
        public List<string> Strings = new List<string>();

        public void Parse(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                // 1. Header
                string headerStr = ReadHeaderString(br);
                uint version = br.ReadUInt32();
                byte endian = br.ReadByte();
                uint userVersion = br.ReadUInt32();
                uint numBlocks = br.ReadUInt32();
                uint bsHeader = br.ReadUInt32();

                // 2. Creator Strings
                for (int i = 0; i < 3; i++)
                {
                    long currentPos = ms.Position;
                    if (currentPos >= ms.Length) break;
                    byte len = br.ReadByte();
                    if (len > 0 && len < 100) { 
                        br.ReadBytes(len); 
                    } else { 
                        ms.Position = currentPos; 
                        break; 
                    }
                }

                // 3. Block Types
                int numBlockTypes = (int)br.ReadUInt16();
                var blockTypes = new List<string>();
                for (int i = 0; i < numBlockTypes; i++)
                {
                    uint len = br.ReadUInt32();
                    blockTypes.Add(Encoding.ASCII.GetString(br.ReadBytes((int)len)));
                }

                // 4. Block Type Indices
                var blockTypeIndices = new ushort[numBlocks];
                for (int i = 0; i < (int)numBlocks; i++)
                    blockTypeIndices[i] = br.ReadUInt16();

                // 5. Block Sizes
                var blockSizes = new uint[numBlocks];
                for (int i = 0; i < (int)numBlocks; i++)
                    blockSizes[i] = br.ReadUInt32();

                // 6. FO3 String Table
                if (ms.Position + 8 <= ms.Length)
                {
                    uint numStrings = br.ReadUInt32();
                    uint maxStringLen = br.ReadUInt32();
                    for (int i = 0; i < numStrings; i++)
                    {
                        uint len = br.ReadUInt32();
                        Strings.Add(Encoding.ASCII.GetString(br.ReadBytes((int)len)).TrimEnd('\0'));
                    }
                }

                // 7. Groups (Roots) - FO3 20.2.0.7: numGroups(uint32), each: groupLen(uint32) + refs(int32*)
                if (ms.Position + 4 <= ms.Length)
                {
                    uint numGroups = br.ReadUInt32();
                    for (int i = 0; i < numGroups; i++)
                    {
                        if (ms.Position + 4 > ms.Length) break;
                        uint groupLen = br.ReadUInt32();
                        for (int j = 0; j < groupLen; j++)
                        {
                            if (ms.Position + 4 > ms.Length) break;
                            int rootIdx = br.ReadInt32();
                            if (rootIdx != -1) RootBlockIndices.Add(rootIdx);
                        }
                    }
                }

                // 8. Blocks
                for (int i = 0; i < (int)numBlocks; i++)
                {
                    uint size = blockSizes[i];
                    string type = (blockTypeIndices[i] < blockTypes.Count) ? blockTypes[blockTypeIndices[i]] : "Unknown";
                    byte[] blockData = br.ReadBytes((int)size);
                    Blocks.Add(new NIFBlock { Index = i, Type = type, Data = blockData });
                }

                // If no groups found, default to block 0
                if (RootBlockIndices.Count == 0 && numBlocks > 0) RootBlockIndices.Add(0);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NIFReader] Parse error: {e.Message}");
            }
        }

        private string ReadHeaderString(BinaryReader br)
        {
            List<byte> bytes = new List<byte>();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                byte b = br.ReadByte();
                bytes.Add(b);
                if (b == 0x0A) break; // \n
                if (bytes.Count > 100) break; // Safety
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }
    }

    public class NIFBlock
    {
        public int Index;
        public string Type;
        public byte[] Data;
    }
}