// Copyright Elliot Bentine, 2018-
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

#pragma warning disable 0672

namespace ProPixelizer
{
    /// <summary>
    /// Reconfigures draw target and projection matrices to force opaques to be rendered to the low-res target.
    /// </summary>
    public class ProPixelizerOpaqueDrawRedirectionPass : ProPixelizerPass
    {
        public ProPixelizerOpaqueDrawRedirectionPass(ProPixelizerLowResolutionConfigurationPass configPass, ProPixelizerLowResolutionTargetPass targetPass)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            ConfigurationPass = configPass;
            TargetPass = targetPass;
        }

        #region non-RG
        ProPixelizerLowResolutionTargetPass TargetPass;
        ProPixelizerLowResolutionConfigurationPass ConfigurationPass;

        bool _currentlyDrawingPreviewCamera;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // workaround for stupid bug: https://forum.unity.com/threads/preview-camera-does-not-call-setuprenderpasses.1377363/
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                FindDrawObjectPasses(renderingData.cameraData.renderer as UniversalRenderer, renderingData.cameraData);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (
                Settings.Filter == PixelizationFilter.FullScene
                && !_currentlyDrawingPreviewCamera
                && Settings.TargetForPixelation == TargetForPixelation.ProPixelizerTarget
                )
            {
                if (OpaqueDrawPass != null && TransparentDrawPass != null)
                {
#pragma warning disable 0618
                    OpaqueDrawPass.ConfigureTarget(TargetPass.Color, TargetPass.Depth);
                    if (SkyboxPass != null)
                        SkyboxPass.ConfigureTarget(TargetPass.Color, TargetPass.Depth);
                    TransparentDrawPass.ConfigureTarget(TargetPass.Color, TargetPass.Depth);
#pragma warning restore 0618
                }
            }
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            // Workaround for DBuffer bug:
            // If DBuffer decals are present on the feature, rendering to a camera with a texture output (like the subcamera)
            // causes an error 'RenderTexture.Create failed: colorFormat & depthStencilFormat cannot both be none.'
            // The error causes the screen to go black, presumably because frame cleanup gets botched. Anyway, always doing frame cleanup,
            // even if we haven't redirected, is a workaround for now.
            // 
            //if (_performedRedirection)
            //{
#pragma warning disable 0618
            if (OpaqueDrawPass != null)
                OpaqueDrawPass.ResetTarget();
            if (TransparentDrawPass != null)
                TransparentDrawPass.ResetTarget();
            if (SkyboxPass != null)
                SkyboxPass.ResetTarget();
#pragma warning restore 0618
        }

        public void FindDrawObjectPasses(UniversalRenderer renderer, in RenderingData renderingData)
        {
            FindDrawObjectPasses(renderer, renderingData.cameraData);
        }

        public void FindDrawObjectPasses(UniversalRenderer renderer, in CameraData cameraData)
        {
            OpaqueDrawPass = OpaqueForwardRendererFieldGetter.GetValue(renderer) as DrawObjectsPass;
            TransparentDrawPass = TransparentForwardRendererFieldGetter.GetValue(renderer) as DrawObjectsPass;
            SkyboxPass = SkyboxFieldGetter.GetValue(renderer) as DrawSkyboxPass;
            _currentlyDrawingPreviewCamera = cameraData.cameraType == CameraType.Preview;
        }

        readonly static FieldInfo OpaqueForwardRendererFieldGetter = typeof(UniversalRenderer).GetField("m_RenderOpaqueForwardPass", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly static FieldInfo TransparentForwardRendererFieldGetter = typeof(UniversalRenderer).GetField("m_RenderTransparentForwardPass", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly static FieldInfo SkyboxFieldGetter = typeof(UniversalRenderer).GetField("m_DrawSkyboxPass", BindingFlags.NonPublic | BindingFlags.Instance);

        private DrawObjectsPass OpaqueDrawPass;
        private DrawObjectsPass TransparentDrawPass;
        private DrawSkyboxPass SkyboxPass;
        public static FieldInfo DrawPassFilterSettingsField = typeof(DrawObjectsPass).GetField("m_FilteringSettings", BindingFlags.NonPublic | BindingFlags.Instance);

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer buffer = CommandBufferPool.Get(nameof(ProPixelizerOpaqueDrawRedirectionPass));
            if (renderingData.cameraData.cameraType == CameraType.Preview) // preview camera badly bugged, doesnt call all URP frame methods
            {
                buffer.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);
                buffer.SetRenderTarget(TargetPass.Color, TargetPass.Depth);
                buffer.ClearRenderTarget(true, true, renderingData.cameraData.camera.backgroundColor);
                context.ExecuteCommandBuffer(buffer);
                CommandBufferPool.Release(buffer);
                return;
            }
            
            if (Settings.Filter == PixelizationFilter.FullScene)
            {
                buffer.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);
                buffer.SetRenderTarget(TargetPass.Color, TargetPass.Depth);
                if (renderingData.cameraData.camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base)
                    buffer.ClearRenderTarget(true, true, renderingData.cameraData.camera.backgroundColor);
                else
                    buffer.ClearRenderTarget(true, false, renderingData.cameraData.camera.backgroundColor);
                ProPixelizerUtils.SetLowResViewProjectionMatrices(
                    buffer,
                    ref renderingData.cameraData,
                    ConfigurationPass.Data.OrthoLowResWidth,
                    ConfigurationPass.Data.OrthoLowResHeight,
                    ConfigurationPass.Data.LowResCameraDeltaWS,
                    ConfigurationPass.Data.LowResPerspectiveVerticalFOV,
                    ConfigurationPass.Data.LowResAspect,
                    Settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera
                    );
                // Required for Forward+ to work; see #255
                float width = ConfigurationPass.Data.Width;
                float height = ConfigurationPass.Data.Height;
                buffer.SetGlobalVector("_ScaledScreenParams", new Vector4(width, height, 1.0f + 1.0f / width, 1.0f + 1.0f / height));
            }
            else
                ProPixelizerUtils.SetPixelGlobalScale(buffer, 0.01f); // disable pixel size in other cases - draw objects wont be matching metadata buffer.

            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);

            var pixelizedLayerMask = renderingData.cameraData.cameraType != CameraType.Preview ? Settings.PixelizedLayers.value : int.MaxValue;
            var cullingMask = Settings.Filter != PixelizationFilter.Layers ? renderingData.cameraData.camera.cullingMask : renderingData.cameraData.camera.cullingMask & ~pixelizedLayerMask;

            if (OpaqueDrawPass != null)
            {
                // change opaque and transparent passes so that they do not draw the pixelated layers.
                var opaqueFilteringSettings = (FilteringSettings)DrawPassFilterSettingsField.GetValue(OpaqueDrawPass);
                opaqueFilteringSettings.layerMask = cullingMask;
                DrawPassFilterSettingsField.SetValue(OpaqueDrawPass, opaqueFilteringSettings);
            }

            if (TransparentDrawPass != null)
            {
                var transparentFilteringSettings = (FilteringSettings)DrawPassFilterSettingsField.GetValue(TransparentDrawPass);
                //different culling mask for transparents?
                transparentFilteringSettings.layerMask = cullingMask;
                DrawPassFilterSettingsField.SetValue(TransparentDrawPass, transparentFilteringSettings);
            }
        }
        #endregion

#if UNITY_2023_3_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // nothing - no effect in RG
        }
#endif
    }
}