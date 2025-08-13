// Copyright Elliot Bentine, 2018-
using System;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEngine.GraphicsBuffer;
#if UNITY_2023_3_OR_NEWER
using ProPixelizer.RenderGraphResources;
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
    /// <summary>
    /// todo
    /// </summary>
    public class ProPixelizerApplyPixelizationMapPass : ProPixelizerPass
    {
        public ProPixelizerApplyPixelizationMapPass(
            ShaderResources shaders,
            ProPixelizerMetadataPass metadata,
            ProPixelizerLowResolutionConfigurationPass configPass,
            ProPixelizerLowResolutionTargetPass targetPass,
            ProPixelizerPixelizationMapPass mapPass
            )
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            Materials = new MaterialLibrary(shaders);
            ConfigPass = configPass;
            TargetPass = targetPass;
            MetadataPass = metadata;
            MapPass = mapPass;
        }

        private const string ApplyPixelMapDepthOutputKeywordString = "PIXELMAP_DEPTH_OUTPUT_ON";
        private readonly GlobalKeyword ApplyPixelMapDepthOutputKeyword = GlobalKeyword.Create(ApplyPixelMapDepthOutputKeywordString);
        private MaterialLibrary Materials;

        #region non-RG
        private ProPixelizerLowResolutionConfigurationPass ConfigPass;
        private ProPixelizerLowResolutionTargetPass TargetPass;
        private ProPixelizerPixelizationMapPass MapPass;
        private ProPixelizerMetadataPass MetadataPass;


        private RTHandle _OriginalScene;
        private RTHandle _PixelatedScene;
        private RTHandle _PixelatedScene_Depth;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
#if DISABLE_PROPIX_PREVIEW_WINDOW
            // Inspector window in 2022.2 is bugged with a null render target.
            // https://forum.unity.com/threads/preview-camera-does-not-call-setuprenderpasses.1377363/
            // https://forum.unity.com/threads/nullreferenceexception-in-2022-2-1f1-urp-14-0-4-when-rendering-the-inspector-preview-window.1377240/
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
#endif
#pragma warning disable 0618
            ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
#pragma warning restore 0618
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.useMipMap = false;
            if (ConfigPass.Data.Width > 0)
            {
                cameraTextureDescriptor.width = ConfigPass.Data.Width;
                cameraTextureDescriptor.height = ConfigPass.Data.Height;
            }

            var depthDescriptor = cameraTextureDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
            depthDescriptor.depthBufferBits = 32;

#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref _PixelatedScene_Depth, depthDescriptor, name: "ProP_PixelatedScene_Depth", wrapMode: TextureWrapMode.Clamp);
            cameraTextureDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _PixelatedScene, cameraTextureDescriptor, name: "ProP_PixelatedScene");
            RenderingUtils.ReAllocateIfNeeded(ref _OriginalScene, cameraTextureDescriptor, name: "ProP_OriginalScene", wrapMode: TextureWrapMode.Clamp);
#pragma warning restore 0618
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer command = CommandBufferPool.Get(GetType().Name);
            var usePixelExpansion = Settings.UsePixelExpansion && renderingData.cameraData.cameraType != CameraType.Preview;

            // ExecutePass
            ExecutePass(
                command,
                Materials.ApplyPixelizationMap,
                Materials.CopyDepth,
                usePixelExpansion,
                ColorTargetForPixelization(ref renderingData, TargetPass),
                DepthTargetForPixelization(ref renderingData, TargetPass),
                _OriginalScene,
                _PixelatedScene,
                _PixelatedScene_Depth,
                MapPass.PixelizationMap,
                MetadataPass.MetadataObjectBuffer_Depth,
                ApplyPixelMapDepthOutputKeyword
                );

            // ...and restore transformations:
            ProPixelizerUtils.SetCameraMatrices(command, ref renderingData.cameraData);

            context.ExecuteCommandBuffer(command);
            CommandBufferPool.Release(command);
        }

        public void Dispose()
        {
            _PixelatedScene?.Release();
            _PixelatedScene_Depth?.Release();
            _OriginalScene?.Release();
        }
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public Material applyPixelizationMap;
            public Material copyDepth;
            public bool usePixelExpansion;
            public TextureHandle ColorTarget;
            public TextureHandle ColorTargetDepth;
            public TextureHandle pointFilteredColorTarget;
            public TextureHandle PixelatedScene;
            public TextureHandle PixelatedSceneDepth;
            public TextureHandle PixelizationMap;
            public TextureHandle MetadataDepth;
            public GlobalKeyword depthOutputKeyword;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var metadataTarget = frameData.Get<MetadataTargetData>();
            var lowResConfig = frameData.Get<ProPixelizerTargetConfigurationContextItem>().Config;
            var lowResTarget = frameData.Get<LowResTargetData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var mapData = frameData.Get<PixelizationMapData>();
            var resources = frameData.Get<UniversalResourceData>();

            var colorDescriptor = lowResTarget.ColorDescriptor(resources.cameraColor.GetDescriptor(renderGraph), lowResConfig);
            var depthDescriptor = lowResTarget.DepthDescriptor(resources.cameraDepth.GetDescriptor(renderGraph), lowResConfig);

            colorDescriptor.clearBuffer = true;
            colorDescriptor.filterMode = FilterMode.Point;
            colorDescriptor.wrapMode = TextureWrapMode.Clamp;

            depthDescriptor.clearBuffer = true;
            depthDescriptor.filterMode = FilterMode.Point;
            depthDescriptor.wrapMode = TextureWrapMode.Clamp;

            var pfColorTargetDescriptor = colorDescriptor;
            pfColorTargetDescriptor.name = "ProPixelizer_PointFilteredColorTarget";
            var pfColorTarget = renderGraph.CreateTexture(pfColorTargetDescriptor);
            
            var pixelatedSceneDescriptor = colorDescriptor;
            pixelatedSceneDescriptor.name = "ProPixelizer_PixelatedScene";
            var pixelatedScene = renderGraph.CreateTexture(pixelatedSceneDescriptor);

            var pixelatedSceneDepthDescriptor = depthDescriptor;
            pixelatedSceneDepthDescriptor.name = "ProPixelizer_PixelatedSceneDepth";
            var pixelatedSceneDepth = renderGraph.CreateTexture(pixelatedSceneDepthDescriptor);

            TextureHandle inputColor, inputDepth;
            switch (Settings.TargetForPixelation)
            {
                default:
                    inputColor = lowResTarget.Color;
                    inputDepth = lowResTarget.Depth;
                    break;
                case TargetForPixelation.CameraTarget:
                    inputColor = resources.cameraColor;
                    inputDepth = resources.cameraDepth;
                    break;
            }

            using (var builder = renderGraph.AddUnsafePass<PassData>(GetType().Name, out var passData))
            {
                passData.applyPixelizationMap = Materials.ApplyPixelizationMap;
                passData.copyDepth = Materials.CopyDepth;
                passData.usePixelExpansion = Settings.UsePixelExpansion;
                passData.ColorTarget = inputColor;
                passData.ColorTargetDepth = inputDepth;
                passData.pointFilteredColorTarget = pfColorTarget;
                passData.PixelatedScene = pixelatedScene;
                passData.PixelatedSceneDepth = pixelatedSceneDepth;
                passData.PixelizationMap = mapData.Map;
                passData.depthOutputKeyword = ApplyPixelMapDepthOutputKeyword;
                passData.MetadataDepth = metadataTarget.Depth;

                builder.UseTexture(passData.ColorTarget, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ColorTargetDepth, AccessFlags.ReadWrite);
                builder.UseTexture(passData.pointFilteredColorTarget, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PixelatedScene, AccessFlags.Write);
                builder.UseTexture(passData.PixelatedSceneDepth, AccessFlags.Write);
                builder.UseTexture(passData.PixelizationMap, AccessFlags.Read);
                builder.UseTexture(passData.MetadataDepth, AccessFlags.Read);

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    ExecutePass(
                        CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                        data.applyPixelizationMap,
                        data.copyDepth,
                        data.usePixelExpansion,
                        data.ColorTarget,
                        data.ColorTargetDepth,
                        data.pointFilteredColorTarget,
                        data.PixelatedScene,
                        data.PixelatedSceneDepth,
                        data.PixelizationMap,
                        data.MetadataDepth,
                        data.depthOutputKeyword
                        );
                });
            }
        }

#endif
        #endregion

        public static void ExecutePass(
            CommandBuffer buffer,
            Material applyPixelizationMap,
            Material copyDepth,
            bool usePixelExpansion,
            RTHandle ColorTarget, // TargetPass.Color
            RTHandle ColorTargetDepth, // TargetPass.Depth
            RTHandle PointFilteredColorTarget,
            RTHandle PixelatedScene,
            RTHandle PixelatedSceneDepth,
            RTHandle PixelizationMap,
            RTHandle MetadataDepth,
            GlobalKeyword depthOutputKeyword
            )
        {
            if (!usePixelExpansion)
                return;

            // Blit scene into _OriginalScene - so that we can guarantee point filtering of colors.
            Blitter.BlitCameraTexture(buffer, ColorTarget, PointFilteredColorTarget);

            // Pixelise the appearance texture
            buffer.SetGlobalTexture("_MainTex", PointFilteredColorTarget);
            buffer.SetGlobalTexture("_PixelizationMap", PixelizationMap);
            buffer.SetGlobalTexture("_SourceDepthTexture", MetadataDepth, RenderTextureSubElement.Depth);
            buffer.SetGlobalTexture("_SceneDepthTexture", ColorTargetDepth);
            buffer.DisableKeyword(depthOutputKeyword);
            Blitter.BlitCameraTexture(buffer, PointFilteredColorTarget, PixelatedScene, applyPixelizationMap, 0);
            buffer.EnableKeyword(depthOutputKeyword);
            Blitter.BlitCameraTexture(buffer, ColorTargetDepth, PixelatedSceneDepth, applyPixelizationMap, 0);

            // Blit Pixelised Color and Depth back into the low res target.
            buffer.SetGlobalTexture("_MainTex", PixelatedScene);
            buffer.SetGlobalTexture("_SourceTex", PixelatedScene); // so that it works for fallback pass for platforms that fail to compile BCMT&D
            buffer.SetGlobalTexture("_Depth", PixelatedSceneDepth);
            Blitter.BlitCameraTexture(buffer, PixelatedScene, ColorTarget);
            Blitter.BlitCameraTexture(buffer, PixelatedSceneDepth, ColorTargetDepth, copyDepth, 0);
        }

        private const string CopyDepthShaderName = "Hidden/ProPixelizer/SRP/BlitCopyDepth";
        private const string CopyMainTexAndDepthShaderName = "Hidden/ProPixelizer/SRP/BlitCopyMainTexAndDepth";
        private const string ApplyPixelizationMapShaderName = "Hidden/ProPixelizer/SRP/ApplyPixelizationMap";

        /// <summary>
        /// Shader resources used by the PixelizationPass.
        /// </summary>
        [Serializable]
        public sealed class ShaderResources
        {
            public Shader CopyDepth;
            public Shader CopyMainTexAndDepth;
            public Shader ApplyPixelizationMap;

            public ShaderResources Load()
            {
                CopyDepth = Shader.Find(CopyDepthShaderName);
                ApplyPixelizationMap = Shader.Find(ApplyPixelizationMapShaderName);
                CopyMainTexAndDepth = Shader.Find(CopyMainTexAndDepthShaderName);
                return this;
            }
        }

        /// <summary>
        /// Materials used by the PixelizationPass
        /// </summary>
        public sealed class MaterialLibrary
        {
            private ShaderResources Resources;

            private Material _CopyDepth;
            public Material CopyDepth
            {
                get
                {
                    if (_CopyDepth == null)
                        _CopyDepth = new Material(Resources.CopyDepth);
                    return _CopyDepth;
                }
            }
            private Material _CopyMainTexAndDepth;
            public Material CopyMainTexAndDepth
            {
                get
                {
                    if (_CopyMainTexAndDepth == null)
                        _CopyMainTexAndDepth = new Material(Resources.CopyMainTexAndDepth);
                    return _CopyMainTexAndDepth;
                }
            }
            private Material _ApplyPixelizationMap;
            public Material ApplyPixelizationMap
            {
                get
                {
                    if (_ApplyPixelizationMap == null)
                        _ApplyPixelizationMap = new Material(Resources.ApplyPixelizationMap);
                    return _ApplyPixelizationMap;
                }
            }

            public MaterialLibrary(ShaderResources resources)
            {
                Resources = resources;
            }
        }
    }
}