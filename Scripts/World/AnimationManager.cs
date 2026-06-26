using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFo3.BSA;
using OpenFo3.ESM;
using OpenFo3.NIF;

namespace OpenFo3.World
{
    public class AnimationManager
    {
        private List<(BSAReader BSA, List<BSAFile> Files)> _meshBsaList;
        private Dictionary<string, List<KFAnimationData>> _animCache = new();
        private Dictionary<string, KFAnimationData> _kfCache = new();

        public AnimationManager(List<(BSAReader BSA, List<BSAFile> Files)> meshBsaList)
        {
            _meshBsaList = meshBsaList;
        }

        public List<KFAnimationData> LoadKfAnimations(string kfPath)
        {
            if (string.IsNullOrEmpty(kfPath)) return null;
            kfPath = kfPath.Replace('\\', '/').ToLowerInvariant();

            if (_kfCache.TryGetValue(kfPath, out var single))
                return new List<KFAnimationData> { single };

            if (_animCache.TryGetValue(kfPath, out var cached))
                return cached;

            BSAFile match = null;
            BSAReader owner = null;
            foreach (var (bsa, files) in _meshBsaList)
            {
                match = files.FirstOrDefault(f => f.Path.Equals(kfPath, StringComparison.OrdinalIgnoreCase));
                if (match != null) { owner = bsa; break; }
            }

            if (match == null || owner == null)
            {
                string altPath = kfPath;
                if (!altPath.StartsWith("meshes/"))
                    altPath = "meshes/" + altPath;
                foreach (var (bsa, files) in _meshBsaList)
                {
                    match = files.FirstOrDefault(f => f.Path.Equals(altPath, StringComparison.OrdinalIgnoreCase));
                    if (match != null) { owner = bsa; break; }
                }
                if (match == null || owner == null)
                    return null;
                kfPath = altPath;
            }

            byte[] data = owner.ReadFileData(match);
            if (data == null || data.Length < 20)
                return null;

            var anims = KFAnimationLoader.LoadAnimations(data);
            if (anims.Count > 0)
            {
                if (anims.Count == 1)
                    _kfCache[kfPath] = anims[0];
                else
                    _animCache[kfPath] = anims;
            }
            return anims;
        }

        public AnimationPlayer AttachAnimationPlayer(Node3D targetNode, KFAnimationData animData, SkeletonData skeleton)
        {
            var animPlayer = targetNode.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            if (animPlayer == null)
            {
                animPlayer = new AnimationPlayer();
                animPlayer.Name = "AnimationPlayer";
                targetNode.AddChild(animPlayer);
            }

            var anim = KFAnimationLoader.BuildGodotAnimation(animData, skeleton);
            string animName = animData.Name ?? "anim";

            string libName = "default";
            var lib = animPlayer.GetAnimationLibrary(libName);
            if (lib == null)
            {
                lib = new AnimationLibrary();
                animPlayer.AddAnimationLibrary(libName, lib);
            }
            if (lib.HasAnimation(animName))
                lib.RemoveAnimation(animName);
            lib.AddAnimation(animName, anim);

            return animPlayer;
        }

        public static List<string> GetAnimationPathsFromNif(NIFReader nif)
        {
            var paths = new List<string>();
            foreach (var block in nif.Blocks)
            {
                if (block.Type == "NiTextKeyExtraData")
                {
                    try
                    {
                        using var ms = new MemoryStream(block.Data);
                        using var br = new BinaryReader(ms);
                        br.ReadUInt32(); br.ReadUInt32(); br.ReadInt32();
                        uint numKeys = br.ReadUInt32();
                        for (int i = 0; i < numKeys && br.BaseStream.Position + 4 < br.BaseStream.Length; i++)
                        {
                            br.ReadSingle();
                            uint strLen = br.ReadUInt32();
                            if (strLen > 256 || br.BaseStream.Position + strLen > br.BaseStream.Length) break;
                            string text = System.Text.Encoding.ASCII.GetString(br.ReadBytes((int)strLen)).TrimEnd('\0');
                            if (text.Contains(".kf", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains(".Kf", StringComparison.OrdinalIgnoreCase))
                            {
                                paths.Add(text.Trim().Replace('\\', '/'));
                            }
                        }
                    }
                    catch { }
                }
            }
            return paths;
        }
    }
}
