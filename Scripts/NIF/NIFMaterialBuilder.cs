using Godot;
using System;

namespace OpenFo3.NIF
{
    public static class NIFMaterialBuilder
    {
        private const int SlotDiffuse = 0;
        private const int SlotNormalGloss = 1;
        private const int SlotGlowSkinHair = 2;
        private const int SlotHeightParallax = 3;
        private const int SlotEnvironment = 4;
        private const int SlotEnvironmentMask = 5;
        private const int SlotSubsurface = 6;
        private const int SlotBackLighting = 7;

        // FO3 BSShaderProperty shader types
        private const int ShaderTypeDefault = 0;
        private const int ShaderTypeDefaultAlt = 1;
        private const int ShaderTypeEnvironmentMap = 2;
        private const int ShaderTypeGlowShader = 3;
        private const int ShaderTypeHeightmap = 4;
        private const int ShaderTypeZBufferWrite = 5;
        private const int ShaderTypeLODLandscape = 6;
        private const int ShaderTypeLODBuilding = 7;
        private const int ShaderTypeMultiLayerParallax = 10;
        private const int ShaderTypeParallaxOcc = 11;
        private const int ShaderTypeSnowShader = 14;
        private const int ShaderTypeMultiLayerParallaxOcc = 15;
        private const int ShaderTypeEnvironmentMapFO3 = 17;
        private const int ShaderTypeWing = 29;
        private const int ShaderTypeSkinTint = 31;
        private const int ShaderTypeHairTint = 32;
        private const int ShaderTypeParallaxOccInner = 33;
        private const int ShaderTypeTallGrass = 34;
        private const int ShaderTypeLODLandscapeNoGrass = 35;

        public static StandardMaterial3D BuildMaterial(ShaderTextureInfo shader, AlphaPropertyInfo alpha,
            Func<string, Texture2D> loadTexture, Func<string, bool> textureHasAlpha = null)
        {
            var mat = new StandardMaterial3D();

            if (shader == null)
            {
                mat.AlbedoColor = new Color(0.8f, 0.8f, 0.8f);
                mat.Roughness = 0.8f;
                return mat;
            }

            var texPaths = shader.TexturePaths;
            ApplyAlpha(mat, shader, alpha, texPaths, textureHasAlpha);

            if ((shader.ShaderFlags2 & (1 << 5)) != 0)
                mat.VertexColorUseAsAlbedo = true;

            switch (shader.ShaderType)
            {
                case ShaderTypeGlowShader:
                    BuildGlowShader(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeEnvironmentMap:
                case ShaderTypeEnvironmentMapFO3:
                    BuildEnvironmentMap(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeSkinTint:
                    BuildSkinTint(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeHairTint:
                    BuildHairTint(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeTallGrass:
                    BuildTallGrass(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeMultiLayerParallax:
                case ShaderTypeParallaxOcc:
                case ShaderTypeMultiLayerParallaxOcc:
                case ShaderTypeParallaxOccInner:
                    BuildMultiLayerParallax(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeHeightmap:
                    BuildHeightmap(mat, shader, texPaths, loadTexture);
                    break;
                default:
                    BuildDefault(mat, shader, texPaths, loadTexture);
                    break;
            }

            ApplyRefraction(mat, shader);
            return mat;
        }

        private static void ApplyAlpha(StandardMaterial3D mat, ShaderTextureInfo shader,
            AlphaPropertyInfo alpha, string[] texPaths, Func<string, bool> textureHasAlpha)
        {
            if (alpha != null)
            {
                ushort f = alpha.Flags;
                bool hasAlphaBlend = (f & 0x0001) != 0;
                bool hasAlphaTest = (f & 0x0200) != 0;

                if (hasAlphaBlend)
                    mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                else if (hasAlphaTest)
                {
                    mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                    mat.AlphaScissorThreshold = alpha.Threshold / 255f;
                }
            }
            else if ((shader.ShaderFlags & 0x00000100) != 0)
            {
                bool diffuseHasAlpha = false;
                if (textureHasAlpha != null && texPaths != null &&
                    texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
                {
                    diffuseHasAlpha = textureHasAlpha(texPaths[SlotDiffuse]);
                }
                if (diffuseHasAlpha)
                {
                    mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                    mat.AlphaScissorThreshold = 0.5f;
                }
            }
        }

        private static void ApplyCommonTextures(StandardMaterial3D mat, string[] texPaths,
            Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null) return;

            if (texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null) mat.AlbedoTexture = tex;
            }

            if (texPaths.Length > SlotNormalGloss && !string.IsNullOrEmpty(texPaths[SlotNormalGloss]))
            {
                var tex = loadTexture(texPaths[SlotNormalGloss]);
                if (tex != null)
                {
                    mat.NormalTexture = tex;
                    mat.NormalEnabled = true;
                }
            }

            if (texPaths.Length > SlotEnvironment && !string.IsNullOrEmpty(texPaths[SlotEnvironment]))
            {
                var tex = loadTexture(texPaths[SlotEnvironment]);
                if (tex != null)
                {
                    mat.MetallicTexture = tex;
                    mat.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red;
                }
            }

            if (texPaths.Length > SlotEnvironmentMask && !string.IsNullOrEmpty(texPaths[SlotEnvironmentMask]))
            {
                var tex = loadTexture(texPaths[SlotEnvironmentMask]);
                if (tex != null)
                {
                    mat.RoughnessTexture = tex;
                    mat.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
                }
            }
        }

        private static void BuildDefault(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.6f;
            mat.Metallic = 0.0f;

            if ((shader.ShaderFlags & 0x00000001) != 0)
                mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
            else
                mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;

            if ((shader.ShaderFlags & (1 << 7)) != 0)
            {
                mat.Metallic = 0.3f;
                mat.Roughness = 0.5f;
            }

            ApplyCommonTextures(mat, texPaths, loadTexture);
            ApplyEmission(mat, texPaths, loadTexture);
            ApplyHeightmap(mat, shader, texPaths, loadTexture);
            ApplyDetail(mat, texPaths, loadTexture);
        }

        private static void BuildGlowShader(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.8f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
			mat.DisableAmbientLight = true;

			if (texPaths != null && texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
			{
				var tex = loadTexture(texPaths[SlotDiffuse]);
				if (tex != null)
				{
					mat.AlbedoTexture = tex;
					mat.EmissionTexture = tex;
					mat.EmissionEnabled = true;
					mat.Emission = new Color(1f, 1f, 1f);
				}
			}

			if (texPaths != null && texPaths.Length > SlotGlowSkinHair && !string.IsNullOrEmpty(texPaths[SlotGlowSkinHair]))
            {
                var tex = loadTexture(texPaths[SlotGlowSkinHair]);
                if (tex != null)
                {
                    mat.EmissionTexture = tex;
                    mat.EmissionEnabled = true;
                    mat.Emission = new Color(1f, 1f, 1f);
                }
            }
        }

        private static void BuildEnvironmentMap(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.3f;
            mat.Metallic = 0.8f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;

            if (texPaths == null) return;

            if (texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null) mat.AlbedoTexture = tex;
            }

            if (texPaths.Length > SlotNormalGloss && !string.IsNullOrEmpty(texPaths[SlotNormalGloss]))
            {
                var tex = loadTexture(texPaths[SlotNormalGloss]);
                if (tex != null)
                {
                    mat.NormalTexture = tex;
                    mat.NormalEnabled = true;
                }
            }

            if (texPaths.Length > SlotEnvironment && !string.IsNullOrEmpty(texPaths[SlotEnvironment]))
            {
                var tex = loadTexture(texPaths[SlotEnvironment]);
                if (tex != null)
                {
                    mat.MetallicTexture = tex;
                    mat.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red;
                }
            }

            if (texPaths.Length > SlotEnvironmentMask && !string.IsNullOrEmpty(texPaths[SlotEnvironmentMask]))
            {
                var tex = loadTexture(texPaths[SlotEnvironmentMask]);
                if (tex != null)
                {
                    mat.RoughnessTexture = tex;
                    mat.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
                }
            }
        }

        private static void BuildSkinTint(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.5f;
            mat.Metallic = 0.0f;
			mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
			mat.SubsurfScatterEnabled = true;
			mat.SubsurfScatterSkinMode = true;
			mat.SubsurfScatterStrength = 0.3f;
			mat.SubsurfScatterTransmittanceEnabled = true;
			mat.VertexColorUseAsAlbedo = true;

			if (texPaths == null) return;

			if (texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
			{
				var tex = loadTexture(texPaths[SlotDiffuse]);
				if (tex != null) mat.AlbedoTexture = tex;
			}

			if (texPaths.Length > SlotNormalGloss && !string.IsNullOrEmpty(texPaths[SlotNormalGloss]))
			{
				var tex = loadTexture(texPaths[SlotNormalGloss]);
				if (tex != null)
				{
					mat.NormalTexture = tex;
					mat.NormalEnabled = true;
				}
			}

			if (texPaths.Length > SlotGlowSkinHair && !string.IsNullOrEmpty(texPaths[SlotGlowSkinHair]))
			{
				var tex = loadTexture(texPaths[SlotGlowSkinHair]);
				if (tex != null)
				{
					var img = tex.GetImage();
					if (img != null)
						mat.SubsurfScatterTransmittanceColor = img.GetPixel(0, 0);
				}
			}
        }

        private static void BuildHairTint(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.7f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
            mat.VertexColorUseAsAlbedo = true;

            if (texPaths == null) return;

            if (texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null) mat.AlbedoTexture = tex;
            }

            if (texPaths.Length > SlotNormalGloss && !string.IsNullOrEmpty(texPaths[SlotNormalGloss]))
            {
                var tex = loadTexture(texPaths[SlotNormalGloss]);
                if (tex != null)
                {
                    mat.NormalTexture = tex;
                    mat.NormalEnabled = true;
                }
            }
        }

        private static void BuildTallGrass(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.9f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
            mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            mat.AlphaScissorThreshold = 0.3f;

            if (texPaths == null) return;

            if (texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null) mat.AlbedoTexture = tex;
            }

            if (texPaths.Length > SlotNormalGloss && !string.IsNullOrEmpty(texPaths[SlotNormalGloss]))
            {
                var tex = loadTexture(texPaths[SlotNormalGloss]);
                if (tex != null)
                {
                    mat.NormalTexture = tex;
                    mat.NormalEnabled = true;
                }
            }
        }

        private static void BuildMultiLayerParallax(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            BuildDefault(mat, shader, texPaths, loadTexture);

            if (texPaths != null && texPaths.Length > SlotHeightParallax && !string.IsNullOrEmpty(texPaths[SlotHeightParallax]))
            {
                var tex = loadTexture(texPaths[SlotHeightParallax]);
                if (tex != null)
                {
                    mat.HeightmapTexture = tex;
                    mat.HeightmapEnabled = true;
                    mat.HeightmapScale = shader.ParallaxScale > 0 ? shader.ParallaxScale : 0.05f;
                    mat.HeightmapMaxLayers = Mathf.Max(1, (int)shader.ParallaxMaxPasses);
                }
            }

            if (texPaths != null && texPaths.Length > SlotSubsurface && !string.IsNullOrEmpty(texPaths[SlotSubsurface]))
            {
                var tex = loadTexture(texPaths[SlotSubsurface]);
                if (tex != null)
                {
                    mat.DetailAlbedo = tex;
                    mat.DetailEnabled = true;
                }
            }
        }

        private static void BuildHeightmap(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            BuildDefault(mat, shader, texPaths, loadTexture);

            if (texPaths != null && texPaths.Length > SlotHeightParallax && !string.IsNullOrEmpty(texPaths[SlotHeightParallax]))
            {
                var tex = loadTexture(texPaths[SlotHeightParallax]);
                if (tex != null)
                {
                    mat.HeightmapTexture = tex;
                    mat.HeightmapEnabled = true;
                    mat.HeightmapScale = shader.ParallaxScale > 0 ? shader.ParallaxScale : 0.05f;
                }
            }
        }

        private static void ApplyEmission(StandardMaterial3D mat, string[] texPaths,
            Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null) return;

            string glowPath = null;
            if (texPaths.Length > SlotGlowSkinHair && !string.IsNullOrEmpty(texPaths[SlotGlowSkinHair]))
                glowPath = texPaths[SlotGlowSkinHair];

            if (!string.IsNullOrEmpty(glowPath))
            {
                var tex = loadTexture(glowPath);
                if (tex != null)
                {
                    mat.EmissionTexture = tex;
                    mat.EmissionEnabled = true;
                    mat.Emission = new Color(1f, 1f, 1f);
                }
            }
        }

        private static void ApplyHeightmap(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null) return;

            if (texPaths.Length > SlotHeightParallax && !string.IsNullOrEmpty(texPaths[SlotHeightParallax]))
            {
                var tex = loadTexture(texPaths[SlotHeightParallax]);
                if (tex != null)
                {
                    mat.HeightmapTexture = tex;
                    mat.HeightmapEnabled = true;
                    mat.HeightmapScale = shader.ParallaxScale > 0 ? shader.ParallaxScale : 0.05f;
                    mat.HeightmapMaxLayers = Mathf.Max(1, (int)shader.ParallaxMaxPasses);
                }
            }
        }

        private static void ApplyDetail(StandardMaterial3D mat, string[] texPaths,
            Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null) return;

            if (texPaths.Length > SlotSubsurface && !string.IsNullOrEmpty(texPaths[SlotSubsurface]))
            {
                var tex = loadTexture(texPaths[SlotSubsurface]);
                if (tex != null)
                {
                    mat.DetailAlbedo = tex;
                    mat.DetailEnabled = true;
                }
            }
        }

        private static void ApplyRefraction(StandardMaterial3D mat, ShaderTextureInfo shader)
        {
            if ((shader.ShaderFlags & (1 << 15)) != 0 || (shader.ShaderFlags & (1 << 16)) != 0)
            {
                mat.RefractionEnabled = true;
                mat.RefractionScale = shader.RefractionStrength > 0 ? shader.RefractionStrength : 0.05f;
            }
        }
    }
}
