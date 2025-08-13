// Copyright Elliot Bentine, 2018-
#if UNITY_2022_3_OR_NEWER

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Legacy;
using static UnityEditor.Rendering.Universal.ShaderGraph.SubShaderUtils;
using static Unity.Rendering.Universal.ShaderUtils;
using UnityEditor.Rendering.Universal.ShaderGraph;
using UnityEditor;
using UnityEditor.Rendering.Universal;
using System.Linq;
using UnityEngine.Rendering.Universal;
using System.Reflection;
using UnityEditor.ShaderGraph.Internal;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace ProPixelizer.Universal.ShaderGraph
{
    class ProPixelizerSubTarget : UniversalSubTarget, ILegacyTarget
    {
        static readonly GUID kSourceCodeGuid = new GUID("4c8c5b55e54009246a543f08ebc553bd"); // matches .cs
        static readonly string DefaultLightingRampPath = Includes.PackagePath + "/Ramps/LightingRamp.png";
        static readonly string DefaultColorGradingLUTPath = Includes.PackagePath + "/Palettes/Palette_GB_dither4x4_lookup.png";

        protected virtual GUID SourceCodeGuid => kSourceCodeGuid;

        public override int latestVersion => 2;

        protected virtual string DisplayName => "ProPixelizer";
        private readonly string ProPixelizerRenderType = "ProPixelizer";
        protected virtual bool UseProPixelizerRenderType => true;
        protected virtual bool IncludeMetadataPass => true;

        public ProPixelizerSubTarget()
        {
            displayName = DisplayName;
            ShaderGraphLightingRamp = AssetDatabase.LoadAssetAtPath<Texture>(DefaultLightingRampPath);
            ShaderGraphColorGradingLUT = AssetDatabase.LoadAssetAtPath<Texture>(DefaultColorGradingLUTPath);
            UseColorGrading = false;
            UseDithering = true;
        }

        public Texture ShaderGraphLightingRamp;
        public Texture ShaderGraphColorGradingLUT;
        public bool UseColorGrading;
        public bool UseDithering;

        protected override ShaderID shaderID => ShaderID.Unknown;

        public override bool IsActive() => true;

        public void SetTargetProperties()
        {
            target.surfaceType = SurfaceType.Opaque;
            target.alphaClip = true;
        }

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(SourceCodeGuid, AssetCollection.Flags.SourceDependency);
            base.Setup(ref context);

            var universalRPType = typeof(UniversalRenderPipelineAsset);
            if (!context.HasCustomEditorForRenderPipeline(universalRPType))
            {
                var gui = typeof(ProPixelizerShaderGraphGUI);
//#if HAS_VFX_GRAPH
//                if (TargetsVFX())
//                    gui = typeof(VFXShaderGraphLitGUI);
//#endif
                context.AddCustomEditorForRenderPipeline(gui.FullName, universalRPType);
            }

            SetTargetProperties();

            // Process SubShaders
            var renderType = UseProPixelizerRenderType ? ProPixelizerRenderType : target.renderType;
            context.AddSubShader(PostProcessSubShader(SubShaders.ProPixelizerSubShader(target, target.renderType, renderType, IncludeMetadataPass, target.renderQueue, target.disableBatching)));
        }

        public override void ProcessPreviewMaterial(Material material)
        {
            //if (target.allowMaterialOverride)
            //{
            //    material.SetFloat(Property.CastShadows, target.castShadows ? 1.0f : 0.0f);
            //    material.SetFloat(Property.ReceiveShadows, target.receiveShadows ? 1.0f : 0.0f);
            //    material.SetFloat(Property.CullMode, (int)target.renderFace);
            //    material.SetFloat(Property.ZWriteControl, (float)target.zWriteControl);
            //    material.SetFloat(Property.ZTest, (float)target.zTestMode);
            //}

            // We always need these properties regardless of whether the material is allowed to override
            // Queue control & offset enable correct automatic render queue behavior
            // Control == 0 is automatic, 1 is user-specified render queue
            material.SetFloat(Property.QueueOffset, 0.0f);
            material.SetFloat(Property.QueueControl, (float)BaseShaderGUI.QueueControl.Auto);
            
            material.SetFloat(ProPixelizerMaterialPropertyReferences.PixelSize, 5f);
            material.SetFloat(ProPixelizerMaterialPropertyReferences.COLOR_GRADING, UseColorGrading ? 1.0f : 0.0f);
            material.SetTexture(ProPixelizerMaterialPropertyReferences.LightingRamp, ShaderGraphLightingRamp);
            material.SetTexture(ProPixelizerMaterialPropertyReferences.PaletteLUT, ShaderGraphColorGradingLUT);

            // call the full unlit material setup function
            ShaderGraphLitGUI.UpdateMaterial(material, MaterialUpdateType.CreatedNewMaterial);
        }

        public override void GetFields(ref TargetFieldContext context)
        {
            base.GetFields(ref context);

            var descs = context.blocks.Select(x => x.descriptor);

            context.AddField(UniversalFields.NormalDropOffTS);
            context.AddField(UniversalFields.Normal, descs.Contains(BlockFields.SurfaceDescription.NormalOS) ||
                descs.Contains(BlockFields.SurfaceDescription.NormalTS) ||
                descs.Contains(BlockFields.SurfaceDescription.NormalWS));
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            foreach (var bd in ProPixelizerBlockFields.GetProPixelizerSubTargetBlockFields())
                context.AddBlock(bd);
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            // if using material control, add the material property to control workflow mode
            if (target.allowMaterialOverride)
            {
                collector.AddFloatProperty(Property.CastShadows, target.castShadows ? 1.0f : 0.0f);
                collector.AddFloatProperty(Property.ReceiveShadows, target.receiveShadows ? 1.0f : 0.0f);
                collector.AddToggleProperty(Property.ZWrite, (target.surfaceType == SurfaceType.Opaque));
                collector.AddFloatProperty(Property.ZWriteControl, (float)target.zWriteControl);
                collector.AddFloatProperty(Property.ZTest, (float)target.zTestMode);    // ztest mode is designed to directly pass as ztest
                collector.AddFloatProperty(Property.CullMode, (float)target.renderFace);
                collector.AddFloatProperty(Property.AlphaClip, 1.0f);
                collector.AddFloatProperty(Property.SrcBlend, 1.0f); 
                collector.AddFloatProperty(Property.DstBlend, 0.0f);
                collector.AddFloatProperty(Property.AlphaToMask, 1.0f);
            }

            // We always need these properties regardless of whether the material is allowed to override other shader properties.
            // Queue control & offset enable correct automatic render queue behavior.  Control == 0 is automatic, 1 is user-specified.
            // We initialize queue control to -1 to indicate to UpdateMaterial that it needs to initialize it properly on the material.
            collector.AddFloatProperty(Property.QueueOffset, 0.0f);
            collector.AddFloatProperty(Property.QueueControl, -1.0f);

            AddProPixelizerProperties(collector, generationMode);
        }

        public void AddProPixelizerProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            collector.AddShaderProperty(new IntSliderShaderProperty
            {
                hidden = false,
                rangeValues = new Vector2(0, 5),
                value = 5,
                displayName = ProPixelizerMaterialPropertyLabels.PixelSize,
                overrideReferenceName = ProPixelizerMaterialPropertyReferences.PixelSize,
            });

            collector.AddShaderProperty(new Texture2DShaderProperty
            {
                displayName = ProPixelizerMaterialPropertyLabels.PaletteLUT,
                overrideReferenceName = ProPixelizerMaterialPropertyReferences.PaletteLUT,
                value = new SerializableTexture() { texture = ShaderGraphColorGradingLUT }
            });

            collector.AddShaderProperty(new Texture2DShaderProperty
            {
                displayName = ProPixelizerMaterialPropertyLabels.LightingRamp,
                overrideReferenceName = ProPixelizerMaterialPropertyReferences.LightingRamp,
                useTilingAndOffset = false,
                value = new SerializableTexture() { texture = ShaderGraphLightingRamp }
            });

            collector.AddShaderProperty(new IntSliderShaderProperty
            {
                hidden = false,
                value = 1,
                displayName = ProPixelizerMaterialPropertyLabels.ID,
                overrideReferenceName = ProPixelizerMaterialPropertyReferences.ID,
                rangeValues = new Vector2(0, 255),
            });

            collector.AddShaderProperty(new KeywordToggleShaderProperty
            {
                value = false,
                hidden = false,
                displayName = ProPixelizerMaterialPropertyLabels.UseColorGrading,
                overrideReferenceName = ProPixelizerMaterialKeywordStrings.COLOR_GRADING,
                overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.DoNotDeclare
            });

            collector.AddShaderProperty(new KeywordToggleShaderProperty
            {
                value = false,
                hidden = false,
                displayName = ProPixelizerMaterialPropertyLabels.UseDithering,
                overrideReferenceName = ProPixelizerMaterialKeywordStrings.PROPIXELIZER_DITHERING,
                overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.DoNotDeclare
            });

            collector.AddShaderProperty(new Vector4ShaderProperty
            {
                hidden = false,
                displayName = ProPixelizerMaterialPropertyLabels.PixelGridOrigin,
                overrideReferenceName = ProPixelizerMaterialPropertyReferences.PixelGridOrigin
            });

            collector.AddShaderProperty(new ColorShaderProperty
            {
                hidden = false,
                value = new Color(0.1f, 0.1f, 0.1f, 0.5f),
                displayName = ProPixelizerMaterialPropertyLabels.AmbientLight,
                overrideReferenceName = ProPixelizerMaterialPropertyReferences.AmbientLight,
                colorMode = ColorMode.Default,                
            });
        }

        /// <summary>
        /// Draw the GUI for the Graph Inspector in ShaderGraph
        /// </summary>
        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<String> registerUndo)
        {
            SetTargetProperties();

            var universalTarget = (target as UniversalTarget);
            universalTarget.AddDefaultMaterialOverrideGUI(ref context, onChange, registerUndo);

            AddDefaultSurfaceProperties(ref context, onChange, registerUndo);

            var ProPixelizerHeader = new Label();
            ProPixelizerHeader.enableRichText = true;
            ProPixelizerHeader.text = "<b>ProPixelizer Properties</b>";
            context.Add(ProPixelizerHeader);
            var docButton = new Button(() => Application.OpenURL(ProPixelizerURPConstants.SHADERGRAPH_USER_GUIDE_URL));
            docButton.tooltip = "Opens the User Guide.";
            docButton.text = "SubTarget User Guide";
            context.Add(docButton);


            context.AddProperty("Lighting Ramp",
                new ObjectField()
                {
                    value = ShaderGraphLightingRamp,
                    allowSceneObjects = false,
                    objectType = typeof(Texture),
                    tooltip = "Lighting Ramp to be used in ShaderGraph Editor"
                },
                (evt) =>
                {
                    if (Equals(ShaderGraphLightingRamp, evt.newValue)) return;

                    var tex = evt.newValue as Texture;
                    if (tex == null) return;

                    registerUndo("Change Lighting Ramp");
                    ShaderGraphLightingRamp = tex;
                    onChange();
                });

            context.AddProperty("Use Color Grading", new Toggle() { value = UseColorGrading }, (evt) =>
            {
                if (Equals(UseColorGrading, evt.newValue))
                    return;

                registerUndo("Change Use Color Grading");
                UseColorGrading = evt.newValue;
                onChange();
            });

            context.AddProperty("Color Grading LUT",
                new ObjectField()
                {
                    value = ShaderGraphColorGradingLUT,
                    allowSceneObjects = false,
                    objectType = typeof(Texture),
                    tooltip = "Look up table for color grading in ShaderGraph Editor"
                },
                (evt) =>
                {
                    if (Equals(ShaderGraphColorGradingLUT, evt.newValue)) return;

                    var tex = evt.newValue as Texture;
                    if (tex == null) return;

                    registerUndo("Change Color Grading LUT");
                    ShaderGraphColorGradingLUT = tex;
                    onChange();
                });


            context.AddProperty("Use Dithering", new Toggle() { value = UseDithering }, (evt) =>
            {
                if (Equals(UseDithering, evt.newValue))
                    return;

                registerUndo("Change Use Dithering");
                UseDithering = evt.newValue;
                onChange();
            });
        }

        public void AddDefaultSurfaceProperties(ref TargetPropertyGUIContext context, Action onChange, Action<String> registerUndo)
        {
            context.AddProperty("Render Face", new EnumField(RenderFace.Front) { value = target.renderFace }, (evt) =>
            {
                if (Equals(target.renderFace, evt.newValue))
                    return;

                registerUndo("Change Render Face");
                target.renderFace = (RenderFace)evt.newValue;
                onChange();
            });

            context.AddProperty("Depth Write", new EnumField(ZWriteControl.Auto) { value = target.zWriteControl }, (evt) =>
            {
                if (Equals(target.zWriteControl, evt.newValue))
                    return;

                registerUndo("Change Depth Write Control");
                target.zWriteControl = (ZWriteControl)evt.newValue;
                onChange();
            });

            //context.AddProperty("Alpha Clipping", new Toggle() { value = alphaClip }, (evt) =>
            //{
            //    if (Equals(alphaClip, evt.newValue))
            //        return;

            //    registerUndo("Change Alpha Clip");
            //    alphaClip = evt.newValue;
            //    onChange();
            //});

            context.AddProperty("Cast Shadows", new Toggle() { value = target.castShadows }, (evt) =>
            {
                if (Equals(target.castShadows, evt.newValue))
                    return;

                registerUndo("Change Cast Shadows");
                target.castShadows = evt.newValue;
                onChange();
            });

            context.AddProperty("Receive Shadows", new Toggle() { value = target.receiveShadows }, (evt) =>
            {
                if (Equals(target.receiveShadows, evt.newValue))
                    return;

                registerUndo("Change Receive Shadows");
                target.receiveShadows = evt.newValue;
                onChange();
            });
        }

        protected override int ComputeMaterialNeedsUpdateHash()
        {
            int hash = base.ComputeMaterialNeedsUpdateHash();
            hash = hash * 23 + target.allowMaterialOverride.GetHashCode();
            return hash;
        }

        public bool TryUpgradeFromMasterNode(IMasterNode1 masterNode, out Dictionary<BlockFieldDescriptor, int> blockMap)
        {
            blockMap = null;
            return false;
        }

        internal override void OnAfterParentTargetDeserialized()
        {
            Assert.IsNotNull(target);
        }

        #region SubShader
        static class SubShaders
        {
            public static SubShaderDescriptor ProPixelizerSubShader(
                UniversalTarget target,
                string litRenderType,
                string finalRenderType,
                bool includeMetadataPass,
                string renderQueue,
                string disableBatchingTag
                )
            {
                // Eurgh - why is everything private!
                // Use reflection to create a SubShader using the UniversalLitSubTarget as inspiration.
                var name = typeof(UniversalLitSubTarget).FullName + "+SubShaders," + typeof(UniversalLitSubTarget).Assembly.FullName;
                Type type = Type.GetType(name);
                MethodInfo method = type.GetMethod("LitSubShader", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var ssd = (SubShaderDescriptor)method.Invoke(null, new object[] { target, WorkflowMode.Specular, litRenderType, renderQueue, disableBatchingTag, false, false });

                // Replace the forward pass with ProPixelizer's own.
                PassCollection passes = new PassCollection();
                foreach (var pass in ssd.passes)
                {
                    PassDescriptor pd = pass.descriptor;
                    if (pass.descriptor.lightMode == "UniversalForward") // I hate this is a magic string, but there is no const defined in URP for this...
                    {
                        pd.includes = Includes.ProPixelizerForward;

                        // Add outline color to frag blocks
                        Array.Resize(ref pd.validPixelBlocks, pd.validPixelBlocks.Length + 3);
                        pd.validPixelBlocks[pd.validPixelBlocks.Length - 3] = ProPixelizerBlockFields.ProPixelizerSurfaceDescription.OutlineColor;
                        pd.validPixelBlocks[pd.validPixelBlocks.Length - 2] = ProPixelizerBlockFields.ProPixelizerSurfaceDescription.EdgeBevelWeight;
                        pd.validPixelBlocks[pd.validPixelBlocks.Length - 1] = ProPixelizerBlockFields.ProPixelizerSurfaceDescription.EdgeHighlight;

                        // Field dependency required for world space.
                        pd.requiredFields.Add(StructFields.Varyings.screenPosition);
                        pd.requiredFields.Add(StructFields.SurfaceDescriptionInputs.ScreenPosition); // for dither pattern
                        pd.requiredFields.Add(StructFields.SurfaceDescriptionInputs.WorldSpaceTangent); // bevel
                        pd.requiredFields.Add(StructFields.SurfaceDescriptionInputs.WorldSpaceBiTangent); // bevel
                        pd.requiredFields.Add(StructFields.SurfaceDescriptionInputs.WorldSpaceNormal); // bevel

                        // Add color grading keywords
                        pd.keywords.Add(Keywords.ColorGrading);
                        pd.keywords.Add(Keywords.UseDithering);

                        // We need to change the vertex and fragment pragmas.
                        var pragmas = new PragmaCollection();
                        foreach (var pragma in pd.pragmas)
                        {
                            if (pragma.descriptor.value.StartsWith("frag"))
                                pragmas.Add(Pragma.Fragment("ProPixelizer_frag"));
                            else
                                pragmas.Add(pragma.descriptor, pragma.fieldConditions);
                        }
                        pd.pragmas = pragmas;
                    }

                    // For all passes; change the workflow mode to remove metallic support - we only want specular.
                    // Add color grading keywords
                    ChangeKeywordToDefine("_SPECULAR_SETUP", ref pd);
                    ChangeKeywordToDefine("_ALPHATEST_ON", ref pd);

                    passes.Add(pd, pass.fieldConditions);
                }
                ssd.passes = passes;

                // Add the ProPixelizerPass
                if (includeMetadataPass)
                    ssd.passes.Add(Passes.ProPixelizerMetadata(target, CoreBlockMasks.Vertex, CoreBlockMasks.FragmentDepthNormals, CorePragmas.Forward, CoreKeywords.ShadowCaster));
                ssd.renderType = finalRenderType;

                // Enumerate through passes:
                var newPasses = new PassCollection();
                
                foreach (var originalPass in ssd.passes)
                {
                    var pass = originalPass.descriptor;
                    pass.defines.Add(Keywords.ProPixelizerSubTargetDefine, 1);
                    pass.includes.Add(Includes.ProPixelizerSubTargetHelpers);
                    pass.keywords.Add(Keywords.UseObjectPositionForPixelGrid);

                    switch (pass.displayName)
                    {
                        case "SceneSelectionPass":
                        case "ScenePickingPass":
                            // Remove the scene selection and scene picking passes as they are bugged for us, fallbacks work better.
                            break;
                        default:
                            newPasses.Add(pass, originalPass.fieldConditions);
                            break;
                    }

                }

                ssd.passes = newPasses;
                return ssd;
            }
        }

        static void ChangeKeywordToDefine(string referenceName, ref PassDescriptor pd)
        {
            var keywords = new KeywordCollection();
            bool had_specular_keyword = false;
            KeywordDescriptor descriptor = default;
            foreach (var keyword in pd.keywords)
            {
                if (keyword.descriptor.referenceName == referenceName)
                {
                    had_specular_keyword = true;
                    descriptor = keyword.descriptor;
                    continue;
                }
                keywords.Add(keyword.descriptor, keyword.fieldConditions);
            }
            pd.keywords = keywords;
            if (had_specular_keyword)
                pd.defines.Add(
                    descriptor,
                    1);
        }

        #endregion

        #region Passes
        public static class Passes
        {
            public static PassDescriptor ProPixelizerMetadata(
                UniversalTarget target,
                BlockFieldDescriptor[] vertexBlocks,
                BlockFieldDescriptor[] pixelBlocks,
                PragmaCollection pragmas,
                KeywordCollection keywords)
            {
                var result = new PassDescriptor
                {
                    // Definition
                    displayName = "ProPixelizerPass",
                    referenceName = "(111)", // Normally this would be a defined name from ShaderPass.hlsl, e.g. SHADERPASS_FORWARD where `#define SHADERPASS_FORWARD (0)`.
                    lightMode = "ProPixelizer",
                    useInPreview = true,

                    // Template
                    passTemplatePath = UniversalTarget.kUberTemplatePath,
                    sharedTemplateDirectories = UniversalTarget.kSharedTemplateDirectories,

                    // Port Mask
                    validVertexBlocks = vertexBlocks,
                    validPixelBlocks = pixelBlocks,

                    // Fields
                    structs = CoreStructCollections.Default,
                    requiredFields = CoreRequiredFields.ShadowCaster,
                    fieldDependencies = CoreFieldDependencies.Default,

                    // Conditional State
                    renderStates = CoreRenderStates.UberSwitchedRenderState(target),
                    pragmas = CorePragmas.Instanced,
                    defines = new DefineCollection { },
                    keywords = new KeywordCollection { CoreKeywords.ShadowCaster, Keywords.UseDithering },
                    includes = new IncludeCollection { Includes.ProPixelizerMetadata },

                    // Custom Interpolator Support
                    customInterpolators = CoreCustomInterpDescriptors.Common
                };
                result.defines.Add(CoreKeywordDescriptors.AlphaTestOn, 1);
                CorePasses.AddLODCrossFadeControlToPass(ref result, target);
                return result;
            }
        }
        #endregion

        #region Keywords
        public static class Keywords
        {
            public static readonly KeywordDescriptor ColorGrading = new KeywordDescriptor()
            {
                displayName = "Color Grading",
                referenceName = ProPixelizerMaterialKeywordStrings.COLOR_GRADING_ON,
                type = KeywordType.Boolean,
                definition = KeywordDefinition.ShaderFeature,
                scope = KeywordScope.Local,
                stages = KeywordShaderStage.Fragment
            };

            public static readonly KeywordDescriptor UseDithering = new KeywordDescriptor()
            {
                displayName = "Use Dithering",
                referenceName = ProPixelizerMaterialKeywordStrings.PROPIXELIZER_DITHERING_ON,
                type = KeywordType.Boolean,
                definition = KeywordDefinition.ShaderFeature,
                scope = KeywordScope.Local,
                stages = KeywordShaderStage.Fragment
            };

            public static readonly KeywordDescriptor UseObjectPositionForPixelGrid = new KeywordDescriptor()
            {
                displayName = "Use Object Position for Pixel Grid",
                referenceName = ProPixelizerMaterialKeywordStrings.USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON,
                type = KeywordType.Boolean,
                definition = KeywordDefinition.MultiCompile,
                scope = KeywordScope.Local,
                stages = KeywordShaderStage.All
            };

            /// <summary>
            /// Defines `PROPIXELIZER_SUBTARGET` which identifies the shader has been generated by the ProPixelizer Subtarget.
            /// 
            /// Used only as a define, not as a keyword.
            ///
            /// This workaround is required because there is no convenient way to test if a shader
            /// is currently in a shadergraph preview or is in a complete (generated) output.
            /// </summary>
            public static readonly KeywordDescriptor ProPixelizerSubTargetDefine = new KeywordDescriptor()
            {
                displayName = "ProPixelizer SubTarget",
                referenceName = "PROPIXELIZER_SUBTARGET",
                type = KeywordType.Boolean,
                definition = KeywordDefinition.MultiCompile,
                scope = KeywordScope.Local,
                stages = KeywordShaderStage.Fragment
            };
        }
        #endregion

        #region Includes
        public static class Includes
        {
            private const string MetadataPassPath = "/ShaderGraph/Includes/ProPixelizerMetadataPass.hlsl";
            private const string ProPixelizerForwardPassPath = "/ShaderGraph/Includes/ProPixelizerForwardPass.hlsl";
            private const string PackingUtilsPath = "/SRP/ShaderLibrary/PackingUtils.hlsl";
            private const string PixelUtilsPath = "/SRP/ShaderLibrary/PixelUtils.hlsl";
            private const string OutlineUtilsPath = "/SRP/ShaderLibrary/OutlineUtils.hlsl";
            private const string ScreenUtils = "/SRP/ShaderLibrary/ScreenUtils.hlsl";
            private const string ColorGrading = "/SRP/ShaderLibrary/ColorGrading.hlsl";
            public const string PackagePath = "Assets/ProPixelizer";
            private const string SGSubTargetHelpers = "/SRP/ShaderLibrary/SGSubTargetHelpers.hlsl";
            private const string ShaderGraphUtils = "/SRP/ShaderLibrary/ShaderGraphUtils.hlsl";

            const string kForwardPass = "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/PBRForwardPass.hlsl";
            const string kShadows = "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl";

            public static readonly IncludeCollection ProPixelizerForward = new IncludeCollection
            {
                // Pre-graph
                { CoreIncludes.DOTSPregraph },
                { CoreIncludes.WriteRenderLayersPregraph },
                { CoreIncludes.CorePregraph },
                { kShadows, IncludeLocation.Pregraph },
                { CoreIncludes.ShaderGraphPregraph },
                { CoreIncludes.DBufferPregraph },

                // Post-graph
                { CoreIncludes.CorePostgraph },
                { kForwardPass, IncludeLocation.Postgraph },
                { PackagePath + OutlineUtilsPath, IncludeLocation.Postgraph },
                { PackagePath + ScreenUtils, IncludeLocation.Postgraph },
                //{ PackagePath + PixelUtilsPath, IncludeLocation.Postgraph },
                { PackagePath + ColorGrading, IncludeLocation.Postgraph },
                { PackagePath + ProPixelizerForwardPassPath, IncludeLocation.Postgraph },
            };


            public static readonly IncludeCollection ProPixelizerMetadata = new IncludeCollection
            {
                // Pre-graph
                { CoreIncludes.DOTSPregraph },
                { CoreIncludes.CorePregraph },
                { CoreIncludes.ShaderGraphPregraph },
                { PackagePath + ShaderGraphUtils, IncludeLocation.Pregraph },

                // Post-graph
                { CoreIncludes.CorePostgraph },
                { PackagePath + PackingUtilsPath, IncludeLocation.Pregraph },
                { PackagePath + PixelUtilsPath, IncludeLocation.Pregraph },
                { PackagePath + MetadataPassPath, IncludeLocation.Postgraph },
            };

            public static readonly IncludeCollection ProPixelizerSubTargetHelpers = new IncludeCollection
            {
                // TargetHelpers must come after pregraph, so that material properties are defined.
                { PackagePath + SGSubTargetHelpers, IncludeLocation.Graph }, 
            };
        }
        #endregion
    }
}
#endif