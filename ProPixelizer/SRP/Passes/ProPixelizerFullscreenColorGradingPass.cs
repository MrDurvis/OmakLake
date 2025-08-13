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
    /// Applies a color grading palette to the full screen.
    /// </summary>
    public class ProPixelizerFullscreenColorGradingPass : ProPixelizerPass
	{
		public ProPixelizerFullscreenColorGradingPass(ShaderResources shaders, ProPixelizerMetadataPass metadata)
		{
			renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
			Materials = new MaterialLibrary(shaders);
            _Metadata = metadata;
        }

        const string ColorGradingLUTPropertyName = "_ColorGradingLUT";
        private MaterialLibrary Materials;
        #region non-RG
        private RTHandle _Color;
        private ProPixelizerMetadataPass _Metadata;
        
        /// <summary>
        /// Returns true if full screen color grading is active.
        /// </summary>
        public bool Active => Settings.FullscreenLUT != null;

		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (Active)
                Materials.FullScreenColorGradeMaterial.SetTexture(ColorGradingLUTPropertyName, Settings.FullscreenLUT);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.depthBufferBits = 0;
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref _Color, cameraTextureDescriptor, name: ProPixelizerTargets.PROPIXELIZER_FULLSCREEN_COLOR_GRADING);
            ConfigureTarget(_Color);
            ConfigureClear(ClearFlag.None, Color.black);
#pragma warning restore 0618
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!Active) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;

            var colorTarget = ColorTarget(ref renderingData);
            if (colorTarget == null) return;

            CommandBuffer command = CommandBufferPool.Get(GetType().Name);
            ExecutePass(
                command,
                Materials.FullScreenColorGradeMaterial,
                colorTarget,
                _Color,
                _Metadata.MetadataObjectBuffer,
                Active
                );
            context.ExecuteCommandBuffer(command);
            CommandBufferPool.Release(command);
        }

        public void Dispose()
        {
            if (!Active) return;
            _Color?.Release();
        }
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public Material colorGradingMaterial;
            public TextureHandle colorTarget;
            public TextureHandle temporaryColorTarget;
            public TextureHandle metadata;
            public bool active;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Materials.FullScreenColorGradeMaterial.SetTexture(ColorGradingLUTPropertyName, Settings.FullscreenLUT);
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var metadata = frameData.Get<MetadataTargetData>();

            var descriptor = CameraColorTexture(resources, cameraData).GetDescriptor(renderGraph);
            descriptor.depthBufferBits = 0;
            descriptor.name = ProPixelizerTargets.PROPIXELIZER_FULLSCREEN_COLOR_GRADING;
            descriptor.clearBuffer = false;

            var tempColor = renderGraph.CreateTexture(descriptor);

            using (var builder = renderGraph.AddUnsafePass<PassData>(GetType().Name, out var passData))
            {
                passData.colorGradingMaterial = Materials.FullScreenColorGradeMaterial;
                passData.colorTarget = CameraColorTexture(resources, cameraData);
                passData.temporaryColorTarget = tempColor;
                passData.metadata = metadata.Color;
                passData.active = Active;

                builder.UseTexture(passData.temporaryColorTarget, AccessFlags.ReadWrite);
                builder.UseTexture(passData.colorTarget, AccessFlags.ReadWrite);
                builder.UseTexture(passData.metadata, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(
                    (PassData data, UnsafeGraphContext context) =>
                    {
                        ExecutePass(
                            CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                            data.colorGradingMaterial,
                            data.colorTarget,
                            data.temporaryColorTarget,
                            data.metadata,
                            data.active
                            );
                    }
                );
            }
        }

#endif
#endregion

        public static void ExecutePass(
            CommandBuffer command,
            Material colorGradingMaterial,
            RTHandle colorTarget,
            RTHandle temporaryColorTarget,
            RTHandle metadataObject,
            bool active
            )
        {
            if (!active)
                return;
            command.SetGlobalTexture("_ProPixelizer_Metadata", metadataObject);
            Blitter.BlitCameraTexture(command, colorTarget, temporaryColorTarget, colorGradingMaterial, 0);
            Blitter.BlitCameraTexture(command, temporaryColorTarget, colorTarget);
        }


        /// <summary>
        /// Shader resources used by the PixelizationPass.
        /// </summary>
        [Serializable]
		public sealed class ShaderResources
		{
			public Shader FullScreenColorGrade;
            private const string FullScreenColorGradeName = "Hidden/ProPixelizer/SRP/FullscreenColorGrade";

            public ShaderResources Load()
			{
                FullScreenColorGrade = Shader.Find(FullScreenColorGradeName);
                return this;
			}
		}

		/// <summary>
		/// Materials used by the PixelizationPass
		/// </summary>
		public sealed class MaterialLibrary
		{
			private ShaderResources Resources;

			private Material _FullScreenColorGradeMaterial;
			public Material FullScreenColorGradeMaterial
            {
				get
				{
					if (_FullScreenColorGradeMaterial == null)
                        _FullScreenColorGradeMaterial = new Material(Resources.FullScreenColorGrade);
					return _FullScreenColorGradeMaterial;
				}
			}

            public MaterialLibrary(ShaderResources resources)
			{
				Resources = resources;
			}
		}
	}
}