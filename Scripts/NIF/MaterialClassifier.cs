using Godot;
using System;
using System.Collections.Generic;

namespace OpenFo3.NIF
{
    public enum MaterialClass
    {
        Unclassified = 0,
        Stone,          // 石材（コンクリート、岩）
        Metal,          // 金属（鉄、鋼鉄）
        HeavyMetal,     // 重金属（装甲板、金庫）
        Wood,           // 木材
        Glass,          // ガラス
        Plastic,        // プラスチック/合成樹脂
        Fabric,         // 布
        Organic,        // 有機物（皮膚、肉）
        Dirt,           // 土/砂
        Water,          // 水
        Snow,           // 雪
        Skin,           // 皮膚（人物）
        Foliage,        // 草/葉
        Ceramic,        // 陶器
        Paper,          // 紙
        Rubber,         // ゴム
    }

    public class MaterialDefinition
    {
        public MaterialClass Class;
        public float Roughness;
        public float Metallic;
        public float EmissionStrength;
        public Color AlbedoTint;
        public bool Transparent;
        public BaseMaterial3D.SpecularModeEnum SpecularMode;
        public BaseMaterial3D.BlendModeEnum BlendMode;
        public bool SubsurfaceScattering;
        public float SubsurfaceStrength;
    }

    public static class MaterialClassifier
    {
        private static readonly Dictionary<string, MaterialClass> _pathMaterialMap = new()
        {
            // Architecture materials
            { "architecture", MaterialClass.Stone },
            { "concrete", MaterialClass.Stone },
            { "stone", MaterialClass.Stone },
            { "brick", MaterialClass.Stone },
            { "dungeon", MaterialClass.Stone },
            { "ruins", MaterialClass.Stone },
            { "cave", MaterialClass.Stone },
            { "rock", MaterialClass.Stone },

            // Metal
            { "metal", MaterialClass.Metal },
            { "steel", MaterialClass.Metal },
            { "iron", MaterialClass.HeavyMetal },
            { "pipe", MaterialClass.Metal },
            { "wire", MaterialClass.Metal },
            { "chain", MaterialClass.Metal },
            { "girder", MaterialClass.HeavyMetal },
            { "railing", MaterialClass.Metal },
            { "ladder", MaterialClass.Metal },
            { "gate", MaterialClass.HeavyMetal },
            { "cage", MaterialClass.Metal },
            { "vent", MaterialClass.Metal },
            { "duct", MaterialClass.Metal },
            { "conduit", MaterialClass.Metal },
            { "barrel", MaterialClass.Metal },
            { "canister", MaterialClass.Metal },
            { "machine", MaterialClass.Metal },
            { "engine", MaterialClass.Metal },
            { "generator", MaterialClass.Metal },
            { "tank", MaterialClass.HeavyMetal },
            { "turret", MaterialClass.HeavyMetal },
            { "safes", MaterialClass.HeavyMetal },
            { "locker", MaterialClass.Metal },
            { "metalbox", MaterialClass.Metal },

            // Wood
            { "wood", MaterialClass.Wood },
            { "wooden", MaterialClass.Wood },
            { "furniture", MaterialClass.Wood },
            { "shelf", MaterialClass.Wood },
            { "cabinet", MaterialClass.Wood },
            { "counter", MaterialClass.Wood },
            { "table", MaterialClass.Wood },
            { "chair", MaterialClass.Wood },
            { "crate", MaterialClass.Wood },
            { "barricade", MaterialClass.Wood },
            { "fence", MaterialClass.Wood },
            { "poster", MaterialClass.Paper },

            // Glass
            { "glass", MaterialClass.Glass },
            { "window", MaterialClass.Glass },
            { "bulb", MaterialClass.Glass },
            { "lightbulb", MaterialClass.Glass },
            { "lamp", MaterialClass.Glass },
            { "bottle", MaterialClass.Glass },
            { "beaker", MaterialClass.Glass },
            { "vial", MaterialClass.Glass },

            // Plastic / Synthetic
            { "plastic", MaterialClass.Plastic },
            { "synthetic", MaterialClass.Plastic },
            { "rubber", MaterialClass.Rubber },
            { "vinyl", MaterialClass.Plastic },
            { "pipe_plastic", MaterialClass.Plastic },

            // Fabric / Cloth
            { "fabric", MaterialClass.Fabric },
            { "cloth", MaterialClass.Fabric },
            { "clothing", MaterialClass.Fabric },
            { "carpet", MaterialClass.Fabric },
            { "rug", MaterialClass.Fabric },
            { "curtain", MaterialClass.Fabric },
            { "tarp", MaterialClass.Fabric },
            { "cloth_", MaterialClass.Fabric },
            { "sleepingbag", MaterialClass.Fabric },
            { "mattress", MaterialClass.Fabric },

            // Organic
            { "organic", MaterialClass.Organic },
            { "flesh", MaterialClass.Organic },
            { "meat", MaterialClass.Organic },
            { "corpse", MaterialClass.Organic },
            { "body", MaterialClass.Organic },
            { "brain", MaterialClass.Organic },
            { "intestines", MaterialClass.Organic },
            { "torso", MaterialClass.Organic },
            { "arm", MaterialClass.Organic },
            { "leg", MaterialClass.Organic },
            { "hand", MaterialClass.Organic },
            { "head", MaterialClass.Organic },

            // Foliage / Nature
            { "foliage", MaterialClass.Foliage },
            { "tree", MaterialClass.Foliage },
            { "plant", MaterialClass.Foliage },
            { "grass", MaterialClass.Foliage },
            { "leaf", MaterialClass.Foliage },
            { "bush", MaterialClass.Foliage },
            { "flower", MaterialClass.Foliage },
            { "mushroom", MaterialClass.Foliage },
            { "vine", MaterialClass.Foliage },

            // Dirt / Ground
            { "dirt", MaterialClass.Dirt },
            { "soil", MaterialClass.Dirt },
            { "ground", MaterialClass.Dirt },
            { "mud", MaterialClass.Dirt },
            { "sand", MaterialClass.Dirt },
            { "gravel", MaterialClass.Dirt },
            { "asphalt", MaterialClass.Stone },
            { "road", MaterialClass.Stone },
            { "sidewalk", MaterialClass.Stone },
            { "pavement", MaterialClass.Stone },

            // Water
            { "water", MaterialClass.Water },
            { "puddle", MaterialClass.Water },
            { "pool", MaterialClass.Water },
            { "liquid", MaterialClass.Water },

            // Snow
            { "snow", MaterialClass.Snow },
            { "ice", MaterialClass.Glass },
            { "frost", MaterialClass.Snow },

            // Ceramic / Porcelain
            { "ceramic", MaterialClass.Ceramic },
            { "porcelain", MaterialClass.Ceramic },
            { "toilet", MaterialClass.Ceramic },
            { "sink", MaterialClass.Ceramic },
            { "tub", MaterialClass.Ceramic },
            { "tile", MaterialClass.Ceramic },

            // Paper
            { "paper", MaterialClass.Paper },
            { "note", MaterialClass.Paper },
            { "book", MaterialClass.Paper },
            { "magazine", MaterialClass.Paper },
            { "folder", MaterialClass.Paper },
            { "file", MaterialClass.Paper },
        };

        private static readonly Dictionary<MaterialClass, MaterialDefinition> _materialDefs = new()
        {
            [MaterialClass.Unclassified] = new MaterialDefinition
            {
                Class = MaterialClass.Unclassified,
                Roughness = 0.6f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(1, 1, 1),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            [MaterialClass.Stone] = new MaterialDefinition
            {
                Class = MaterialClass.Stone,
                Roughness = 0.85f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.85f, 0.82f, 0.78f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            [MaterialClass.Metal] = new MaterialDefinition
            {
                Class = MaterialClass.Metal,
                Roughness = 0.35f,
                Metallic = 0.85f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.75f, 0.75f, 0.78f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            },
            [MaterialClass.HeavyMetal] = new MaterialDefinition
            {
                Class = MaterialClass.HeavyMetal,
                Roughness = 0.45f,
                Metallic = 0.9f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.4f, 0.4f, 0.42f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            },
            [MaterialClass.Wood] = new MaterialDefinition
            {
                Class = MaterialClass.Wood,
                Roughness = 0.9f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.65f, 0.5f, 0.35f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            [MaterialClass.Glass] = new MaterialDefinition
            {
                Class = MaterialClass.Glass,
                Roughness = 0.05f,
                Metallic = 0.1f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.85f, 0.9f, 0.95f),
                Transparent = true,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            },
            [MaterialClass.Plastic] = new MaterialDefinition
            {
                Class = MaterialClass.Plastic,
                Roughness = 0.4f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.8f, 0.8f, 0.82f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            },
            [MaterialClass.Fabric] = new MaterialDefinition
            {
                Class = MaterialClass.Fabric,
                Roughness = 0.95f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.7f, 0.65f, 0.6f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            [MaterialClass.Organic] = new MaterialDefinition
            {
                Class = MaterialClass.Organic,
                Roughness = 0.7f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.6f, 0.3f, 0.25f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
                SubsurfaceScattering = true,
                SubsurfaceStrength = 0.2f,
            },
            [MaterialClass.Dirt] = new MaterialDefinition
            {
                Class = MaterialClass.Dirt,
                Roughness = 0.95f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.5f, 0.42f, 0.3f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            [MaterialClass.Water] = new MaterialDefinition
            {
                Class = MaterialClass.Water,
                Roughness = 0.05f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.2f, 0.3f, 0.4f),
                Transparent = true,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            },
            [MaterialClass.Snow] = new MaterialDefinition
            {
                Class = MaterialClass.Snow,
                Roughness = 0.9f,
                Metallic = 0.0f,
                EmissionStrength = 0.1f,
                AlbedoTint = new Color(1.3f, 1.3f, 1.35f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            [MaterialClass.Skin] = new MaterialDefinition
            {
                Class = MaterialClass.Skin,
                Roughness = 0.5f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(1, 1, 1),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
                SubsurfaceScattering = true,
                SubsurfaceStrength = 0.3f,
            },
            [MaterialClass.Foliage] = new MaterialDefinition
            {
                Class = MaterialClass.Foliage,
                Roughness = 0.9f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.5f, 0.7f, 0.3f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            [MaterialClass.Ceramic] = new MaterialDefinition
            {
                Class = MaterialClass.Ceramic,
                Roughness = 0.2f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.9f, 0.9f, 0.92f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            },
            [MaterialClass.Paper] = new MaterialDefinition
            {
                Class = MaterialClass.Paper,
                Roughness = 0.95f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.85f, 0.82f, 0.75f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            [MaterialClass.Rubber] = new MaterialDefinition
            {
                Class = MaterialClass.Rubber,
                Roughness = 0.8f,
                Metallic = 0.0f,
                EmissionStrength = 0.0f,
                AlbedoTint = new Color(0.2f, 0.2f, 0.22f),
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
        };

        private static readonly Dictionary<uint, MaterialClass> _havokMaterialMap = new()
        {
            { 0, MaterialClass.Stone },      // Stone
            { 1, MaterialClass.Fabric },     // Cloth
            { 2, MaterialClass.Dirt },       // Dirt
            { 3, MaterialClass.Glass },      // Glass
            { 4, MaterialClass.Foliage },    // Grass
            { 5, MaterialClass.Metal },      // Metal
            { 6, MaterialClass.Organic },    // Organic
            { 7, MaterialClass.Skin },       // Skin
            { 8, MaterialClass.Water },      // Water
            { 9, MaterialClass.Wood },       // Wood
            { 10, MaterialClass.Stone },     // Heavy Stone
            { 11, MaterialClass.HeavyMetal },// Heavy Metal
            { 12, MaterialClass.Wood },      // Heavy Wood
            { 13, MaterialClass.Metal },     // Chain
            { 14, MaterialClass.Snow },      // Snow
            { 16, MaterialClass.Metal },     // Hollow Metal
            { 17, MaterialClass.Metal },     // Sheet Metal
            { 18, MaterialClass.Dirt },      // Sand
            { 19, MaterialClass.Stone },     // Broken Concrete
            { 20, MaterialClass.HeavyMetal },// Iron
        };

        public static MaterialClass ClassifyByPath(string nifPath)
        {
            if (string.IsNullOrEmpty(nifPath)) return MaterialClass.Unclassified;

            string lower = nifPath.ToLowerInvariant().Replace('\\', '/');

            // Check path segments for keyword matches
            string[] segments = lower.Split('/', '.', '_', '-');
            foreach (var seg in segments)
            {
                if (_pathMaterialMap.TryGetValue(seg, out var cls))
                    return cls;
            }

            // Check full path for partial matches
            foreach (var kvp in _pathMaterialMap)
            {
                if (lower.Contains(kvp.Key))
                    return kvp.Value;
            }

            return MaterialClass.Unclassified;
        }

        public static MaterialClass ClassifyByHavokMaterial(uint havokMaterial)
        {
            if (_havokMaterialMap.TryGetValue(havokMaterial, out var cls))
                return cls;
            return MaterialClass.Unclassified;
        }

        public static MaterialClass ClassifyByShaderType(int shaderType)
        {
            // Certain shader types imply material category
            switch (shaderType)
            {
                case 31: // SkinTint
                    return MaterialClass.Skin;
                case 34: // TallGrass
                    return MaterialClass.Foliage;
                case 41: // Water
                    return MaterialClass.Water;
                case 14: // SnowShader
                    return MaterialClass.Snow;
                case 29: // Wing
                    return MaterialClass.Organic;
                default:
                    return MaterialClass.Unclassified;
            }
        }

        public static MaterialClass Classify(string nifPath, int shaderType = 0, uint havokMaterial = 0xFFFFFFFF)
        {
            // Priority: shader > havok > path
            var byShader = ClassifyByShaderType(shaderType);
            if (byShader != MaterialClass.Unclassified) return byShader;

            if (havokMaterial != 0xFFFFFFFF)
            {
                var byHavok = ClassifyByHavokMaterial(havokMaterial);
                if (byHavok != MaterialClass.Unclassified) return byHavok;
            }

            return ClassifyByPath(nifPath);
        }

        public static MaterialDefinition GetDefinition(MaterialClass cls)
        {
            if (_materialDefs.TryGetValue(cls, out var def))
                return def;
            return _materialDefs[MaterialClass.Unclassified];
        }
    }
}
