using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;

namespace RetroShadersPro.URP.Editor
{
    internal class RetroLitShaderGUI : ShaderGUI
    {
        private RetroShaderProperty baseColor = new("_BaseColor", "Base Color", "Albedo color of the object.");
        private RetroShaderProperty baseTex = new ("_BaseMap", "Base Texture", "Albedo texture of the object.");
        private RetroShaderProperty normalTex = new("_NormalMap", "Normal Texture", "A texture which changes the direction of the normal vector while calculating lighting.");
        private RetroShaderProperty normalStrength = new("_NormalStrength", "Normal Strength", "Changes the strength of the normal offsets produced by the normal texture.");
        private RetroShaderProperty colorBitDepth = new("_ColorBitDepth", "Color Depth", "Limits the total number of values used for each color channel.");
        private RetroShaderProperty colorBitDepthOffset = new("_ColorBitDepthOffset", "Color Depth Offset", "Increase this value if the bit depth offset makes your object too dark.");
        private RetroShaderProperty resolutionLimit = new("_ResolutionLimit", "Resolution Limit", "Limits the resolution of the texture to this value." +
            "\nNote that this setting only snaps the resolution to powers of two." +
            "\nAlso, make sure the Base Texture has mipmaps enabled.");
        private RetroShaderProperty affineTextureStrength = new("_AffineTextureStrength", "Affine Texture Strength", "How strongly the affine texture mapping effect is applied." +
            "\nWhen this is set to 1, the shader uses affine texture mapping exactly like the PS1." +
            "\nWhen this is set to 0, the shader uses perspective-correct texture mapping, like modern systems.");
        private RetroShaderProperty filterMode = new("_FilterMode", "Filter Mode", "Which kind of filtering should the shader use while sampling the base texture?" +
            "\n  Bilinear: Blend between the nearest 4 pixels, which appears smooth." +
            "\n  Point: Use nearest neighbor sampling, which appears blocky." +
            "\n  N64: Use the limited 3-point sampling method from the Nintendo 64.");
        private RetroShaderProperty wrapMode = new("_WrapMode", "Wrap Mode", "Which kind of wrap mode should the shader use while sampling the base texture?" + 
            "\n  Clamp: When UVs exceed the 0-1 range, the color clamps to the nearest edge of the texture." +
            "\n  Repeat: When UVs exceed the 0-1 range, the texture is sampled using the fractional part of the UV coordinates.");
        private RetroShaderProperty ditherMode = new("_DitherMode", "Dither Mode", "How should the shader dither colors which fall between color bit values?" +
            "\n  Screen: Use screen-space coordinates for dithering." +
            "\n    Note that this mode is driven by the pixel size in the CRT post process." +
            "\n  Texture: Use the texture coordinates for dithering." +
            "\n  Off: Don't use any dithering.");
        private RetroShaderProperty useVertexColor = new("_USE_VERTEX_COLORS", "Use Vertex Colors", "Should the base color of the object use vertex coloring?");
        private RetroShaderProperty snappingMode = new("_SnapMode", "Snapping Mode", "Should the shader snap vertices to a limited number of points in space?" +
            "\n  Object: Snap vertices relative to model coordinates." +
            "\n  World: Snap vertices relative to the scene coordinates." +
            "\n  View: Snap vertices relative to the camera coordinates." +
            "\n  Off: Don't do any snapping.");
        private RetroShaderProperty snapsPerUnit = new("_SnapsPerUnit", "Snaps Per Meter",
            "The mesh vertices snap to a limited number of points in space.");
        private RetroShaderProperty lightingMode = new("_LightMode", "Lighting Mode", "Choose how the object should be lit." +
            "\n  Lit: Use per-pixel lighting as standard." +
            "\n  Texel Lit: Snap lighting and shadows to the closest texel on the object's texture." +
            "\n  Vertex Lit: Use per-vertex lighting and interpolate light values for pixels." +
            "\n  Unlit: Don't use lighting calculations (everything is always fully lit).");
        private RetroShaderProperty useFlatShading = new("_USE_FLAT_SHADING", "Use Flat Shading", "Should the shader flatten each triangle of the mesh when shading?");
        private RetroShaderProperty receiveShadowsMode = new("_ReceiveShadowsMode", "Receive Shadows", "Should the object receive shadows from other objects?");
        private RetroShaderProperty ambientToggle = new("_USE_AMBIENT_OVERRIDE", "Ambient Light Override", "Should the object use Unity's default ambient light, or a custom override amount?");
        private RetroShaderProperty ambientLight = new("_AmbientLight", "Ambient Light Strength", "When the ambient light override is used, apply this much ambient light.");
        private RetroShaderProperty useSpecularLight = new("_USE_SPECULAR_LIGHT", "Use Specular Lighting", "Should the shader apply a specular highlight to the object?");
        private RetroShaderProperty glossiness = new("_Glossiness", "Glossiness", "Gloss power value to use for specular lighting. The higher this value is, the smaller the highlight appears on the surface of the object.");
        private RetroShaderProperty useReflectionCubemap = new("_USE_REFLECTION_CUBEMAP", "Use Reflection Cubemap", "Should the shader overlay a cubemap which contains environmental reflections?");
        private RetroShaderProperty reflectionCubemap = new("_ReflectionCubemap", "Reflection Cubemap", "A cubemap which contains environmental reflections.");
        private RetroShaderProperty cubemapColor = new("_CubemapColor", "Cubemap Color", "A color tint applied to the cubemap. The alpha channel acts as a strength multiplier.");
        private RetroShaderProperty cubemapRotation = new("_CubemapRotation", "Cubemap Rotation", "How much to rotate the reflection cubemap around the Y-axis, in degrees.");
        private RetroShaderProperty alphaClip = new("_AlphaClip", "Alpha Clip", "Should the shader clip pixels based on alpha using a threshold value?");
        private RetroShaderProperty alphaClipThreshold = new("_Cutoff", "Threshold", "The threshold value to use for alpha clipping.");
        private RetroShaderProperty cull = new("_Cull", "Render Face", "Should Unity render Front, Back, or Both faces of the mesh?");
        private RetroShaderProperty surfaceType = new("_Surface", "Surface Type", "Should the object be transparent or opaque?");

        private const string alphaTestName = "_ALPHATEST_ON";
        private int srcBlendID;
        private int dstBlendID;
        private int zWriteID;

        private static GUIStyle _boxStyle;
        private static GUIStyle BoxStyle
        {
            get
            {
                return _boxStyle ??= new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 5, 10)
                };
            }
        }

        private static GUIStyle _labelStyle;
        private static GUIStyle LabelStyle
        {
            get
            {
                return _labelStyle ??= new GUIStyle(EditorStyles.boldLabel);
            }
        }

        private enum SurfaceType
        {
            Opaque = 0,
            Transparent = 1
        }

        private enum RenderFace
        {
            Front = 2,
            Back = 1,
            Both = 0
        }

        private SurfaceType activeSurfaceType = SurfaceType.Opaque;
        private RenderFace activeRenderFace = RenderFace.Front;

        protected readonly MaterialHeaderScopeList materialScopeList = new MaterialHeaderScopeList(uint.MaxValue);
        protected MaterialEditor materialEditor;
        private bool firstTimeOpen = true;

        private void FindProperties(MaterialProperty[] props)
        {
            baseColor.prop = FindProperty(baseColor.name, props, true);
            baseTex.prop = FindProperty(baseTex.name, props, true);
            normalTex.prop = FindProperty(normalTex.name, props, true);
            normalStrength.prop = FindProperty(normalStrength.name, props, true);
            resolutionLimit.prop = FindProperty(resolutionLimit.name, props, true);
            snappingMode.prop = FindProperty(snappingMode.name, props, true);
            snapsPerUnit.prop = FindProperty(snapsPerUnit.name, props, true);
            colorBitDepth.prop = FindProperty(colorBitDepth.name, props, true);
            colorBitDepthOffset.prop = FindProperty(colorBitDepthOffset.name, props, true);
            ambientLight.prop = FindProperty(ambientLight.name, props, true);
            affineTextureStrength.prop = FindProperty(affineTextureStrength.name, props, true);
            ambientToggle.prop = FindProperty(ambientToggle.name, props, true);
            filterMode.prop = FindProperty(filterMode.name, props, true);
            wrapMode.prop = FindProperty(wrapMode.name, props, true);
            ditherMode.prop = FindProperty(ditherMode.name, props, true);
            lightingMode.prop = FindProperty(lightingMode.name, props, true);
            useFlatShading.prop = FindProperty(useFlatShading.name, props, true);
            receiveShadowsMode.prop = FindProperty(receiveShadowsMode.name, props, true);
            useVertexColor.prop = FindProperty(useVertexColor.name, props, true);
            useSpecularLight.prop = FindProperty(useSpecularLight.name, props, true);
            glossiness.prop = FindProperty(glossiness.name, props, true);
            useReflectionCubemap.prop = FindProperty(useReflectionCubemap.name, props, true);
            reflectionCubemap.prop = FindProperty(reflectionCubemap.name, props, true);
            cubemapColor.prop = FindProperty(cubemapColor.name, props, true);
            cubemapRotation.prop = FindProperty(cubemapRotation.name, props, true);

            surfaceType.prop = FindProperty(surfaceType.name, props, true);
            cull.prop = FindProperty(cull.name, props, true);
            alphaClip.prop = FindProperty(alphaClip.name, props, true);
            alphaClipThreshold.prop = FindProperty(alphaClipThreshold.name, props, true);
            
            srcBlendID = Shader.PropertyToID("_SrcBlend");
            dstBlendID = Shader.PropertyToID("_DstBlend");
            zWriteID = Shader.PropertyToID("_ZWrite");
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if (materialEditor == null)
            {
                throw new ArgumentNullException("No MaterialEditor found (RetroLitShaderGUI).");
            }

            Material material = materialEditor.target as Material;
            this.materialEditor = materialEditor;

            FindProperties(properties);

            if (firstTimeOpen)
            {
                materialScopeList.RegisterHeaderScope(new GUIContent("Surface Options"), 1u << 0, DrawSurfaceOptions);
                materialScopeList.RegisterHeaderScope(new GUIContent("Retro Properties"), 1u << 1, DrawRetroProperties);
                firstTimeOpen = false;
            }

            materialScopeList.DrawHeaders(materialEditor, material);
            materialEditor.serializedObject.ApplyModifiedProperties();
        }

        private void DrawSurfaceOptions(Material material)
        {
            activeSurfaceType = (SurfaceType)material.GetFloat(surfaceType.id);
            activeRenderFace = (RenderFace)material.GetFloat(cull.id);

            // Display opaque/transparent options.
            bool surfaceTypeChanged = false;
            EditorGUI.BeginChangeCheck();
            {
                activeSurfaceType = (SurfaceType)EditorGUILayout.EnumPopup(surfaceType.info, activeSurfaceType);
            }
            if (EditorGUI.EndChangeCheck())
            {
                surfaceTypeChanged = true;
            }

            // Display culling options.
            EditorGUI.BeginChangeCheck();
            {
                activeRenderFace = (RenderFace)EditorGUILayout.EnumPopup(cull.info, activeRenderFace);
            }
            if (EditorGUI.EndChangeCheck())
            {
                switch (activeRenderFace)
                {
                    case RenderFace.Both:
                        {
                            material.SetFloat(cull.id, 0);
                            break;
                        }
                    case RenderFace.Back:
                        {
                            material.SetFloat(cull.id, 1);
                            break;
                        }
                    case RenderFace.Front:
                        {
                            material.SetFloat(cull.id, 2);
                            break;
                        }
                }
            }

            // Display alpha clip options.
            EditorGUI.BeginChangeCheck();
            {
                materialEditor.ShaderProperty(alphaClip.prop, alphaClip.info);
            }
            if (EditorGUI.EndChangeCheck())
            {
                surfaceTypeChanged = true;
            }

            bool activeAlphaClip;

            if (surfaceTypeChanged)
            {
                switch (activeSurfaceType)
                {
                    case SurfaceType.Opaque:
                        {
                            material.SetOverrideTag("RenderType", "Opaque");
                            material.SetFloat(srcBlendID, (int)UnityEngine.Rendering.BlendMode.One);
                            material.SetFloat(dstBlendID, (int)UnityEngine.Rendering.BlendMode.Zero);
                            material.SetFloat(zWriteID, 1);
                            material.SetFloat(surfaceType.id, 0);

                            activeAlphaClip = material.GetFloat(alphaClip.id) >= 0.5f;
                            if (activeAlphaClip)
                            {
                                material.EnableKeyword(alphaTestName);
                                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                                material.SetOverrideTag("RenderType", "TransparentCutout");
                            }
                            else
                            {
                                material.DisableKeyword(alphaTestName);
                                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                                material.SetOverrideTag("RenderType", "Opaque");
                            }


                            break;
                        }
                    case SurfaceType.Transparent:
                        {
                            activeAlphaClip = material.GetFloat(alphaClip.id) >= 0.5f;
                            if (activeAlphaClip)
                            {
                                material.EnableKeyword(alphaTestName);
                            }
                            else
                            {
                                material.DisableKeyword(alphaTestName);
                            }
                            material.SetOverrideTag("RenderType", "Transparent");
                            material.SetFloat(srcBlendID, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                            material.SetFloat(dstBlendID, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                            material.SetFloat(zWriteID, 0);
                            material.SetFloat(surfaceType.id, 1);

                            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                            break;
                        }
                }
            }

            activeAlphaClip = material.GetFloat(alphaClip.id) >= 0.5f;
            if (activeAlphaClip)
            {
                EditorGUI.indentLevel++;
                materialEditor.ShaderProperty(alphaClipThreshold.prop, alphaClipThreshold.info);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawRetroProperties(Material material)
        {
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(BoxStyle);

            EditorGUILayout.LabelField("Color & Texture Effects", LabelStyle);
            EditorGUILayout.Space(5);

            materialEditor.ShaderProperty(baseColor.prop, baseColor.info);
            materialEditor.ShaderProperty(baseTex.prop, baseTex.info);
            EditorGUILayout.Space(5);
            materialEditor.ShaderProperty(colorBitDepth.prop, colorBitDepth.info);
            materialEditor.ShaderProperty(colorBitDepthOffset.prop, colorBitDepthOffset.info);
            
            if(baseTex.prop.textureValue != null)
            {
                EditorGUILayout.Space(5);
                materialEditor.ShaderProperty(resolutionLimit.prop, resolutionLimit.info);

                if (resolutionLimit.prop.intValue < baseTex.prop.textureValue.width &&
                    resolutionLimit.prop.intValue < baseTex.prop.textureValue.height &&
                    baseTex.prop.textureValue.mipmapCount < 2)
                {
                    EditorGUILayout.HelpBox("Please enable mipmaps on the Base Texture to limit its resolution to a lower value.", MessageType.Warning);
                }
            }

            EditorGUILayout.Space(5);
            materialEditor.ShaderProperty(affineTextureStrength.prop, affineTextureStrength.info);
            EditorGUILayout.Space(5);
            materialEditor.ShaderProperty(filterMode.prop, filterMode.info);
            materialEditor.ShaderProperty(wrapMode.prop, wrapMode.info);
            materialEditor.ShaderProperty(ditherMode.prop, ditherMode.info);
            EditorGUILayout.Space(5);
            materialEditor.ShaderProperty(useVertexColor.prop, useVertexColor.info);

            bool vertexColors = material.GetFloat(useVertexColor.id) >= 0.5f;

            if (vertexColors)
            {
                material.EnableKeyword(useVertexColor.name);
            }
            else
            {
                material.DisableKeyword(useVertexColor.name);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(BoxStyle);

            EditorGUILayout.LabelField("Vertex Snapping", LabelStyle);
            EditorGUILayout.Space(5);

            materialEditor.ShaderProperty(snappingMode.prop, snappingMode.info);

            if (material.GetInteger(snappingMode.id) != 3) // Off.
            {
                EditorGUI.indentLevel++;
                materialEditor.ShaderProperty(snapsPerUnit.prop, snapsPerUnit.info);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(BoxStyle);

            EditorGUILayout.LabelField("Lighting & Shadows", LabelStyle);
            EditorGUILayout.Space(5);
            
            materialEditor.TexturePropertySingleLine(normalTex.info, normalTex.prop, normalStrength.prop);
            materialEditor.ShaderProperty(lightingMode.prop, lightingMode.info);

            int lightMode = material.GetInteger(lightingMode.id);

            if (lightMode != 3) // Unlit.
            {
                materialEditor.ShaderProperty(useFlatShading.prop, useFlatShading.info);

                bool flatShading = material.GetFloat(useFlatShading.id) >= 0.5f;

                if (flatShading)
                {
                    material.EnableKeyword(useFlatShading.name);
                }
                else
                {
                    material.DisableKeyword(useFlatShading.name);
                }

                materialEditor.ShaderProperty(receiveShadowsMode.prop, receiveShadowsMode.info);
                materialEditor.ShaderProperty(ambientToggle.prop, ambientToggle.info);

                bool ambient = material.GetFloat(ambientToggle.id) >= 0.5f;

                if (ambient)
                {
                    material.EnableKeyword(ambientToggle.name);

                    EditorGUI.indentLevel++;
                    materialEditor.ShaderProperty(ambientLight.prop, ambientLight.info);
                    EditorGUI.indentLevel--;
                }
                else
                {
                    material.DisableKeyword(ambientToggle.name);
                }

                materialEditor.ShaderProperty(useSpecularLight.prop, useSpecularLight.info);

                bool useSpecularLighting = material.GetFloat(useSpecularLight.id) >= 0.5f;

                if (useSpecularLighting)
                {
                    material.EnableKeyword(useSpecularLight.name);

                    EditorGUI.indentLevel++;
                    materialEditor.ShaderProperty(glossiness.prop, glossiness.info);
                    EditorGUI.indentLevel--;
                }
                else
                {
                    material.DisableKeyword(useSpecularLight.name);
                }

                materialEditor.ShaderProperty(useReflectionCubemap.prop, useReflectionCubemap.info);

                if(material.GetFloat(useReflectionCubemap.id) >= 0.5f)
                {
                    material.EnableKeyword(useReflectionCubemap.name);

                    EditorGUI.indentLevel++;
                    materialEditor.TexturePropertyWithHDRColor(reflectionCubemap.info, reflectionCubemap.prop, cubemapColor.prop, true);
                    materialEditor.ShaderProperty(cubemapRotation.prop, cubemapRotation.info);
                    EditorGUI.indentLevel--;
                }
                else
                {
                    material.DisableKeyword(useReflectionCubemap.name);
                }
            }

            EditorGUILayout.EndVertical();
        }
    }
}
