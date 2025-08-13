// Copyright Elliot Bentine, 2018-
using System;
using System.Reflection;
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
    /// A pass that blits the desired color and depth targets into all color and depth targets required.
    /// 
    /// These include those used by:
    ///  * The editor scene tab
    ///  * The in-editor preview camera overlay
    ///  * The editor game tab
    ///  * the final game view in build
    ///  * any _Depth or _Opaque targets used for subsequent post-processing and camera attachment.
    /// </summary>
    public class ProPixelizerFinalRecompositionPass : ProPixelizerPass
	{
		public ProPixelizerFinalRecompositionPass(ShaderResources shaders, ProPixelizerLowResRecompositionPass recomp)
		{
			renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
			Materials = new MaterialLibrary(shaders);
			RecompositionPass = recomp;
		}

        private MaterialLibrary Materials;
		#region non-RG
        public ProPixelizerLowResRecompositionPass RecompositionPass;
		

		// Inputs - bookkeeping for now
		private RTHandle _ColorInput;
		private RTHandle _DepthInput;
        private RTHandle _CameraColorTexture;
        private RTHandle _CameraDepthAttachment;
        private RTHandle _CameraDepthAttachmentTemp;

		readonly static FieldInfo CameraDepthTextureGetter = typeof(UniversalRenderer).GetField("m_DepthTexture", BindingFlags.Instance | BindingFlags.NonPublic);

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
    #if UNITY_6000_0_OR_NEWER
            ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle, DepthTarget(ref renderingData));
    #else
            ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
    #endif
#pragma warning restore 0618
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
		{
			_ColorInput = RecompositionPass._Color;
			_DepthInput = RecompositionPass._Depth;

			var depthDescriptor = cameraTextureDescriptor;
            depthDescriptor.useMipMap = false;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
			depthDescriptor.depthBufferBits = 32;
            _CameraColorTexture = colorAttachmentHandle;
            _CameraDepthAttachment = depthAttachmentHandle;
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref _CameraDepthAttachmentTemp, depthDescriptor, name: "ProP_CameraDepthAttachmentTemp");
#pragma warning restore 0618
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
#if DISABLE_PROPIX_PREVIEW_WINDOW
				// Inspector window in 2022.2 is bugged with a null render target.
				// https://forum.unity.com/threads/preview-camera-does-not-call-setuprenderpasses.1377363/
				// https://forum.unity.com/threads/nullreferenceexception-in-2022-2-1f1-urp-14-0-4-when-rendering-the-inspector-preview-window.1377240/
				if (renderingData.cameraData.cameraType == CameraType.Preview)
					return;
#endif

            CommandBuffer buffer = CommandBufferPool.Get(nameof(ProPixelizerFinalRecompositionPass));

			bool isOverlay = renderingData.cameraData.camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Overlay;

			// Start copying ---------------

			// Copy depth into Color target so that transparents work in scene view tab
			buffer.SetGlobalTexture("_MainTex", _ColorInput);
			buffer.SetGlobalTexture("_SourceTex", _ColorInput); // so that it works for fallback pass for platforms that fail to compile BCMT&D
			buffer.SetGlobalTexture("_Depth", _DepthInput);
            Blitter.BlitCameraTexture(buffer, _ColorInput, ColorTarget(ref renderingData));
            Blitter.BlitCameraTexture(buffer, _DepthInput, DepthTarget(ref renderingData), Materials.CopyDepth, 0);
            Blitter.BlitCameraTexture(buffer, _DepthInput, _CameraDepthAttachmentTemp, Materials.CopyDepth, 0);

			// ...then restore transformations:
			ProPixelizerUtils.SetCameraMatrices(buffer, ref renderingData.cameraData);

            //// Blit pixelised depth into the used depth texture
            /// (2022.1b and beyond)
            if (renderingData.cameraData.cameraType != CameraType.Preview)
#pragma warning disable 0618
                Blitter.BlitCameraTexture(buffer, _CameraDepthAttachmentTemp, DepthTarget(ref renderingData), Materials.CopyDepth, 0);
#pragma warning restore 0618

            // Modifying the scene tab's depth:
            // When URP renders the scene tab under UNITY_EDITOR, a final depth pass is enqued.
            // This pass applies CopyDepth of the m_DepthTexture into k_CameraTarget.
            // https://github.com/Unity-Technologies/Graphics/blob/1e4487f95a63937cd3a67d733bd26af31870c77d/Packages/com.unity.render-pipelines.universal/Runtime/UniversalRenderer.cs#L1289
            // So we do need to get m_DepthTexture...
            if (renderingData.cameraData.cameraType == CameraType.SceneView || renderingData.cameraData.cameraType == CameraType.Preview)
			{
				var universalRenderer = renderingData.cameraData.renderer as UniversalRenderer;
				if (universalRenderer != null)
				{
					var cameraDepthTexture = (RTHandle)CameraDepthTextureGetter.GetValue(universalRenderer);
                    if (!isOverlay && cameraDepthTexture != null)
					{
#pragma warning disable 0618
                        Blitter.BlitCameraTexture(buffer, _CameraDepthAttachmentTemp, cameraDepthTexture, Materials.CopyDepth, 0);
                        // Return to the color target to that which we declared in ConfigureTarget.
                        CoreUtils.SetRenderTarget(buffer, 
                            (RenderTargetIdentifier)renderingData.cameraData.renderer.cameraColorTargetHandle,
                            (RenderTargetIdentifier)DepthTarget(ref renderingData)
                            );
#pragma warning restore 0618
                    }
                }
            }

			// ...and restore transformations:
			ProPixelizerUtils.SetCameraMatrices(buffer, ref renderingData.cameraData);
			context.ExecuteCommandBuffer(buffer);
			CommandBufferPool.Release(buffer);
		}

        public void Dispose()
        {
            _CameraDepthAttachmentTemp?.Release();
        }
		#endregion

		#region RG
#if UNITY_2023_3_OR_NEWER

		class PassData
		{
			public TextureHandle RecompositionColor;
			public TextureHandle RecompositionDepth;
			public TextureHandle CameraColor;
            public TextureHandle CameraDepth;
			public TextureHandle CameraDepthTexture;
			public Material copyDepth;
        }

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
			var resources = frameData.Get<UniversalResourceData>();
			var camera = frameData.Get<UniversalCameraData>();
			var recomposition = frameData.Get<ProPixelizerRecompositionData>();

			using (var builder = renderGraph.AddUnsafePass<PassData>(GetType().Name, out var passData))
			{
				passData.RecompositionColor = recomposition.Color;
				passData.RecompositionDepth = recomposition.Depth;
				passData.CameraColor = CameraColorTexture(resources, camera);
				passData.CameraDepth = CameraDepthTexture(resources, camera);
				passData.CameraDepthTexture = resources.cameraDepthTexture;
				passData.copyDepth = Materials.CopyDepth;

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);
				builder.UseTexture(passData.RecompositionColor, AccessFlags.Read);
                builder.UseTexture(passData.RecompositionDepth, AccessFlags.Read);
                builder.UseTexture(passData.CameraColor, AccessFlags.Write);
                builder.UseTexture(passData.CameraDepth, AccessFlags.Write);
				builder.UseTexture(passData.CameraDepthTexture, AccessFlags.Write);
				builder.SetRenderFunc(
					(PassData data, UnsafeGraphContext context) =>
					{
						ExecutePass(
							CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
							data
							);
					});
            }
        }

		static void ExecutePass(CommandBuffer command, PassData data)
		{
            // https://forum.unity.com/threads/introduction-of-render-graph-in-the-universal-render-pipeline-urp.1500833/page-4#post-9570268
            
			Blitter.BlitCameraTexture(command, data.RecompositionColor, data.CameraColor);
            command.SetGlobalTexture("_Depth", data.RecompositionDepth);
            Blitter.BlitCameraTexture(command, data.RecompositionDepth, data.CameraDepth, data.copyDepth, 0);
			if (data.CameraDepthTexture.IsValid())
                Blitter.BlitCameraTexture(command, data.RecompositionDepth, data.CameraDepthTexture, data.copyDepth, 0);
        }

#endif
#endregion

		/// <summary>
		/// Shader resources used by the PixelizationPass.
		/// </summary>
		[Serializable]
		public sealed class ShaderResources
		{
			public Shader CopyDepth;
			public Shader CopyMainTexAndDepth;

            private const string CopyDepthShaderName = "Hidden/ProPixelizer/SRP/BlitCopyDepth";
            private const string CopyMainTexAndDepthShaderName = "Hidden/ProPixelizer/SRP/BlitCopyMainTexAndDepth";

            public ShaderResources Load()
			{
				CopyDepth = Shader.Find(CopyDepthShaderName);
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

			public MaterialLibrary(ShaderResources resources)
			{
				Resources = resources;
			}
		}
	}
}