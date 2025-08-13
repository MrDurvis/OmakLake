// Copyright Elliot Bentine, 2018-
using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using ProPixelizer.RenderGraphResources;
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
    /// <summary>
    /// The recomposition pass is used to merge the low-resolution render of the scene with the high-resolution scene.
    /// The low-resolution texture can either be from a subcamera, or from a ProPixelizer low-resolution target.
    /// 
    /// Recomposition can be made by a few strategies. These include:
    /// - a depth-based merge by comparing the depth buffers of the two targets and selecting the colors which are nearer.
    /// - one of the targets is taken completely. This is _not_ a blit, because we need to map the low-res target into the high
    ///   resolution scene to account for sub-pixel camera movement.
    /// </summary>
    public class ProPixelizerLowResRecompositionPass : ProPixelizerPass
    {
        public ProPixelizerLowResRecompositionPass(ShaderResources resources, ProPixelizerLowResolutionTargetPass targetPass)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            Materials = new MaterialLibrary(resources);
            TargetPass = targetPass;
            RecompositionDepthBased = GlobalKeyword.Create(RECOMPOSITION_DEPTH_BASED);
            RecompositionOnlySecondary = GlobalKeyword.Create(RECOMPOSITION_ONLY_SECONDARY);
            DepthOutputOn = GlobalKeyword.Create(DEPTH_OUTPUT_ON);
            PixelArtAAFilterOn = GlobalKeyword.Create(PIXELART_AA_FILTER_ON);
        }
        private const string RECOMPOSITION_DEPTH_BASED = "RECOMPOSITION_DEPTH_BASED";
        private const string RECOMPOSITION_ONLY_SECONDARY = "RECOMPOSITION_ONLY_SECONDARY";
        private const string DEPTH_OUTPUT_ON = "DEPTH_OUTPUT_ON";
        private const string PIXELART_AA_FILTER_ON = "PIXELART_AA_FILTER_ON";
        private readonly GlobalKeyword RecompositionDepthBased;
        private readonly GlobalKeyword RecompositionOnlySecondary;
        private readonly GlobalKeyword DepthOutputOn;
        private readonly GlobalKeyword PixelArtAAFilterOn;
        readonly ProfilingSampler _ProfilingSampler = new ProfilingSampler(nameof(ProPixelizerLowResRecompositionPass));
        private MaterialLibrary Materials;

        #region non-RG
        private ProPixelizerLowResolutionTargetPass TargetPass;
        public RTHandle _Color;
        public RTHandle _Depth;

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            var colorDescriptor = cameraTextureDescriptor;
            var depthDescriptor = cameraTextureDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
            colorDescriptor.depthBufferBits = 0;
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref _Color, colorDescriptor, name: ProPixelizerTargets.PROPIXELIZER_RECOMPOSITION_COLOR);
            RenderingUtils.ReAllocateIfNeeded(ref _Depth, depthDescriptor, name: ProPixelizerTargets.PROPIXELIZER_RECOMPOSITION_DEPTH);
            ConfigureTarget(_Color, _Depth);
            ConfigureClear(ClearFlag.None, Color.black);
#pragma warning restore 0618
        }

        public void Dispose()
        {
            _Color?.Release();
            _Depth?.Release();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;

            // Blit current color and depth target into the low res targets.
#pragma warning disable 0618
            var useColorTargetForDepth = (renderingData.cameraData.renderer.cameraDepthTargetHandle.nameID == BuiltinRenderTextureType.CameraTarget);
#pragma warning restore 0618
            var depthTarget = useColorTargetForDepth ? ColorTarget(ref renderingData) : DepthTarget(ref renderingData);
            var colorTarget = ColorTarget(ref renderingData);

            // Decide target to use as source
            RTHandle sourceColor, sourceDepth;
            switch (Settings.RecompositionSource)
            {
                case RecompositionLowResSource.ProPixelizerColorTarget:
                default:
                    sourceColor = TargetPass.Color;
                    sourceDepth = TargetPass.Depth;
                    break;
                case RecompositionLowResSource.SubCamera:
                    var subcamera = renderingData.cameraData.camera.GetComponent<ProPixelizerCamera>().SubCamera;
                    if (subcamera == null)
                        return;
                    sourceColor = subcamera.Color;
                    sourceDepth = subcamera.Depth;
                    break;
            }

            CommandBuffer buffer = CommandBufferPool.Get(GetType().Name);
            ExecutePass(
                buffer,
                Materials.SceneRecomposition,
                sourceColor,
                sourceDepth,
                colorTarget,
                depthTarget,
                _Color,
                _Depth,
                Settings.RecompositionMode,
                RecompositionDepthBased,
                RecompositionOnlySecondary,
                DepthOutputOn,
                Settings.UsePixelArtUpscalingFilter,
                PixelArtAAFilterOn
                );

            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public Material sceneRecomposition;
            public TextureHandle lowResColor;
            public TextureHandle lowResDepth;
            public TextureHandle cameraColor;
            public TextureHandle cameraDepth;
            public TextureHandle colorOutput;
            public TextureHandle depthOutput;
            public RecompositionMode mode;
            public GlobalKeyword recompositionDepthKeyword;
            public GlobalKeyword recompositionOnlySecondaryKeyword;
            public GlobalKeyword depthOutputKeyword;
            public bool usePixelArtUpscalingFilter;
            public GlobalKeyword pixelArtAAFilterKeyword;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var lowResTarget = frameData.Get<LowResTargetData>();
            var resources = frameData.Get<UniversalResourceData>();
            var recomposed = frameData.GetOrCreate<ProPixelizerRecompositionData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var originalTargets = frameData.Get<ProPixelizerOriginalCameraTargets>();

            var colorDescriptor = originalTargets.Color.GetDescriptor(renderGraph);
            colorDescriptor.name = ProPixelizerTargets.PROPIXELIZER_RECOMPOSITION_COLOR;
            colorDescriptor.clearBuffer = false;
            var depthDescriptor = originalTargets.Depth.GetDescriptor(renderGraph);
            depthDescriptor.name = ProPixelizerTargets.PROPIXELIZER_RECOMPOSITION_DEPTH;
            depthDescriptor.clearBuffer = false;

            recomposed.Color = renderGraph.CreateTexture(colorDescriptor);
            recomposed.Depth = renderGraph.CreateTexture(depthDescriptor);

            TextureHandle lowResColorSource, lowResDepthSource;
            switch (Settings.RecompositionSource)
            {
                case RecompositionLowResSource.ProPixelizerColorTarget:
                default:
                    lowResColorSource = lowResTarget.Color;
                    lowResDepthSource = lowResTarget.Depth;
                    break;
                case RecompositionLowResSource.SubCamera:
                    var subcameraTargets = frameData.Get<ProPixelizerSubCameraTargets>();
                    lowResColorSource = subcameraTargets.Color;
                    lowResDepthSource = subcameraTargets.Depth;
                    break;
            }

            using (var builder = renderGraph.AddUnsafePass<PassData>(GetType().Name, out var passData))
            {
                passData.sceneRecomposition = Materials.SceneRecomposition;
                passData.lowResColor = lowResColorSource;
                passData.lowResDepth = lowResDepthSource;
                passData.cameraColor = CameraColorTexture(resources, cameraData);
                passData.cameraDepth = CameraDepthTexture(resources, cameraData);
                passData.colorOutput = recomposed.Color;
                passData.depthOutput = recomposed.Depth;
                passData.mode = Settings.RecompositionMode;
                passData.recompositionDepthKeyword = RecompositionDepthBased;
                passData.recompositionOnlySecondaryKeyword = RecompositionOnlySecondary;
                passData.depthOutputKeyword = DepthOutputOn;
                passData.usePixelArtUpscalingFilter = Settings.UsePixelArtUpscalingFilter;
                passData.pixelArtAAFilterKeyword = PixelArtAAFilterOn;

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.UseTexture(passData.lowResColor, AccessFlags.Read);
                builder.UseTexture(passData.lowResDepth, AccessFlags.Read);
                builder.UseTexture(passData.cameraColor, AccessFlags.Read);
                builder.UseTexture(passData.cameraDepth, AccessFlags.Read);
                builder.UseTexture(passData.colorOutput, AccessFlags.Write);
                builder.UseTexture(passData.depthOutput, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    ExecutePass(
                        CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                        data.sceneRecomposition,
                        data.lowResColor,
                        data.lowResDepth,
                        data.cameraColor,
                        data.cameraDepth,
                        data.colorOutput,
                        data.depthOutput,
                        data.mode,
                        data.recompositionDepthKeyword,
                        data.recompositionOnlySecondaryKeyword,
                        data.depthOutputKeyword,
                        data.usePixelArtUpscalingFilter,
                        data.pixelArtAAFilterKeyword
                        );
                });
            }
        }

#endif
        #endregion

        public static void ExecutePass(
            CommandBuffer command,
            Material sceneRecompositionMaterial,
            RTHandle lowResColor,
            RTHandle lowResDepth,
            RTHandle cameraColor,
            RTHandle cameraDepth,
            RTHandle colorOutput,
            RTHandle depthOutput,
            RecompositionMode mode,
            GlobalKeyword recompositionDepthKeyword,
            GlobalKeyword recompositionOnlySecondaryKeyword,
            GlobalKeyword depthOutputKeyword,
            bool usePixelArtUpscalingFilter,
            GlobalKeyword pixelArtAAFilterKeyword
            )
        {
            command.SetGlobalTexture("_InputDepthTexture", cameraDepth, RenderTextureSubElement.Depth);
            command.SetGlobalTexture("_SecondaryTexture", lowResColor);
            command.SetGlobalTexture("_SecondaryDepthTexture", lowResDepth);
            if (usePixelArtUpscalingFilter)
                command.SetKeyword(pixelArtAAFilterKeyword, true);
            else
                command.SetKeyword(pixelArtAAFilterKeyword, false);
            switch (mode) {
                case RecompositionMode.DepthBasedMerge:
                    command.SetKeyword(recompositionDepthKeyword, true);
                    command.SetKeyword(recompositionOnlySecondaryKeyword, false);
                    break;
                default:
                    command.SetKeyword(recompositionDepthKeyword, false);
                    command.SetKeyword(recompositionOnlySecondaryKeyword, true);
                    break;
            }
            if (cameraColor.rt == null)
            {
                // This workaround is required for the small thumbnail preview camera in 6000.0.5f1 (maybe earlier too),
                // which is a preview camera that does not use the backbuffer. Normal preview camera uses backbuffer and is fine.
                Blitter.BlitCameraTexture(command, lowResColor, colorOutput);
                return;
            }
            command.SetGlobalTexture("_Depth", cameraDepth, RenderTextureSubElement.Depth);
            Blitter.BlitCameraTexture(command, cameraColor, colorOutput, sceneRecompositionMaterial, 0);
            command.EnableKeyword(depthOutputKeyword);
            Blitter.BlitCameraTexture(command, cameraDepth, depthOutput, sceneRecompositionMaterial, 0);
            command.DisableKeyword(depthOutputKeyword);
        }



        /// <summary>
        /// Shader resources used by this pass.
        /// </summary>
        [Serializable]
        public sealed class ShaderResources
        {
            private readonly string SceneRecompositionShaderName = "Hidden/ProPixelizer/SRP/Internal/SceneRecomposition";
            public Shader SceneRecomposition;

            public ShaderResources Load()
            {
                SceneRecomposition = Shader.Find(SceneRecompositionShaderName);
                return this;
            }
        }

        /// <summary>
        /// Materials used by this pass.
        /// </summary>
        public sealed class MaterialLibrary
        {
            private ShaderResources Resources;
            public Material SceneRecomposition
            {
                get
                {
                    if (_SceneRecomposition == null)
                        _SceneRecomposition = new Material(Resources.SceneRecomposition);
                    return _SceneRecomposition;
                }
            }
            private Material _SceneRecomposition;

            public MaterialLibrary(ShaderResources resources)
            {
                Resources = resources;
            }
        }
    }
}