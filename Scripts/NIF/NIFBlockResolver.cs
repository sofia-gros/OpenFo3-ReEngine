using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace OpenFo3.NIF
{
    public class NIFBlockResolver
    {
        public class Node
        {
            public string Name;
            public Vector3 Translation;
            public Basis Rotation;
            public float Scale;
            public List<int> Children = new();
            public int DataIndex = -1;
        }

        public static Node Resolve(NIFBlock block)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                if (block.Type == "NiNode" || block.Type == "BSFadeNode" || block.Type == "BSLeafAnimNode" || block.Type == "BSLODNode")
                {
                    return ParseNode(br, block.Index, block.Type);
                }
                else if (block.Type == "NiTriShape" || block.Type == "NiTriStrips")
                {
                    return ParseNode(br, block.Index, block.Type, isGeometry: true);
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NIFBlockResolver] Error resolving block {block.Index} ({block.Type}): {e.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parse NiNode/NiAVObject fields for FO3 NIF version 20.2.0.7.
        /// Verified layout (PyFFI ground truth + binary remainder=0):
        ///
        /// NiObjectNET:
        ///   Name         uint32  (index into string table)
        ///   NumExtraData uint32
        ///   ExtraData[]  int32 * NumExtraData  (block refs)
        ///   Controller   int32  (block ref)
        ///
        /// NiAVObject:
        ///   Flags             uint32
        ///   Translation       float, float, float
        ///   Rotation          9 × float (Matrix33, row-major)
        ///   Scale             float
        ///   NumProperties     uint32
        ///   Properties[]      int32 * NumProperties  (block refs)
        ///   CollisionObject   int32  (block ref)
        ///
        /// NiNode (if not geometry):
        ///   NumChildren   uint32
        ///   Children[]    int32 * NumChildren  (block refs)
        ///   NumEffects    uint32
        ///   Effects[]     int32 * NumEffects   (block refs)
        ///
        /// NiGeometry (if geometry):
        ///   Data            int32  (block ref)
        ///   SkinInstance    int32  (block ref)
        ///   NumMaterials    uint32
        ///   MaterialName[]  int32 * NumMaterials  (block refs)
        ///   MaterialExtra[] int32 * NumMaterials  (block refs)
        ///   ActiveMaterial  uint32
        ///   MaterialNeedsUpdate byte  (FO3 bool = 1 byte)
        /// </summary>
        private static Node ParseNode(BinaryReader br, int blockIdx, string blockType, bool isGeometry = false)
        {
            var node = new Node();

            // 1. NiObjectNET fields
            uint nameIdx = br.ReadUInt32();

            uint numExtra = br.ReadUInt32();
            for (int i = 0; i < numExtra; i++) br.ReadInt32();

            int controllerRef = br.ReadInt32();

            // 2. NiAVObject fields
            uint flags = br.ReadUInt32();

            node.Translation = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

            // Rotation Matrix (3x3, row-major: m11 m12 m13 m21 m22 m23 m31 m32 m33)
            float m11 = br.ReadSingle(), m12 = br.ReadSingle(), m13 = br.ReadSingle();
            float m21 = br.ReadSingle(), m22 = br.ReadSingle(), m23 = br.ReadSingle();
            float m31 = br.ReadSingle(), m32 = br.ReadSingle(), m33 = br.ReadSingle();
            node.Rotation = new Basis(
                new Vector3(m11, m21, m31),
                new Vector3(m12, m22, m32),
                new Vector3(m13, m23, m33)
            );

            node.Scale = br.ReadSingle();

            uint numProps = br.ReadUInt32();
            for (int i = 0; i < numProps; i++) br.ReadInt32();

            int collisionObject = br.ReadInt32();

            // 3. NiNode or NiGeometry fields
            if (isGeometry)
            {
                node.DataIndex = br.ReadInt32(); // Data Ref

                br.ReadInt32(); // Skin Instance Ref

                uint numMaterials = br.ReadUInt32();
                for (int i = 0; i < numMaterials; i++) br.ReadInt32(); // Material Name refs
                for (int i = 0; i < numMaterials; i++) br.ReadInt32(); // Material Extra Data refs

                /* uint activeMaterial = */ br.ReadUInt32();
                br.ReadByte(); // Material Needs Update (bool, 1 byte for FO3)
            }
            else
            {
                uint numChildren = br.ReadUInt32();
                for (int i = 0; i < numChildren; i++)
                {
                    int childIdx = br.ReadInt32();
                    if (childIdx != -1) node.Children.Add(childIdx);
                }

                uint numEffects = br.ReadUInt32();
                for (int i = 0; i < numEffects; i++) br.ReadInt32();
            }

            return node;
        }
    }
}
