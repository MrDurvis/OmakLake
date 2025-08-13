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
using UnityEngine.Rendering.Universal.Internal;

#pragma warning disable 0672

namespace ProPixelizer
{
#if UNITY_2023_3_OR_NEWER
    /// <summary>
    /// Redirects draw calls 
    /// </summary>
    public class ProPixelizerRenderGraphRedirectPass : ProPixelizerPass
    {
        public ProPixelizerRenderGraphRedirectPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public UniversalRenderer _Renderer;
        readonly static FieldInfo OpaqueForwardRendererFieldGetter = typeof(UniversalRenderer).GetField("m_RenderOpaqueForwardPass", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly static FieldInfo TransparentForwardRendererFieldGetter = typeof(UniversalRenderer).GetField("m_RenderTransparentForwardPass", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly static FieldInfo DrawPassFilterSettingsField = typeof(DrawObjectsPass).GetField("m_FilteringSettings", BindingFlags.NonPublic | BindingFlags.Instance);

        class PassData
        {
            public bool preview;
            public ProPixelizerSettings settings;
            public TextureHandle LowResColor;
            public TextureHandle LowResDepth;
            public Color clearColor;
            public bool baseCamera;
            public Matrix4x4 projection;
            public Matrix4x4 view;
            public int LowresTargetWidth;
            public int LowresTargetHeight;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = frameData.Get<UniversalRenderingData>();
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lowRes = frameData.Get<LowResTargetData>();
            var lowResConfig = frameData.Get<ProPixelizerTargetConfigurationContextItem>().Config;

            if (Settings.Filter == PixelizationFilter.FullScene) {
                resources.cameraColor = lowRes.Color;
                resources.cameraDepth = lowRes.Depth;
            }

            // change filter settings for opaques and transparents
            var pixelizedLayerMask = cameraData.cameraType != CameraType.Preview ? Settings.PixelizedLayers.value : int.MaxValue;
            var cullingMask = Settings.Filter != PixelizationFilter.Layers ? cameraData.camera.cullingMask : cameraData.camera.cullingMask & ~pixelizedLayerMask;

            var opaqueDrawPass = OpaqueForwardRendererFieldGetter.GetValue(_Renderer) as DrawObjectsPass;
            var transparentDrawPass = TransparentForwardRendererFieldGetter.GetValue(_Renderer) as DrawObjectsPass;

            var opaqueFilteringSettings = (FilteringSettings)DrawPassFilterSettingsField.GetValue(opaqueDrawPass);
            opaqueFilteringSettings.layerMask = cullingMask;
            DrawPassFilterSettingsField.SetValue(opaqueDrawPass, opaqueFilteringSettings);

            var transparentFilteringSettings = (FilteringSettings)DrawPassFilterSettingsField.GetValue(transparentDrawPass);
            transparentFilteringSettings.layerMask = cullingMask;
            DrawPassFilterSettingsField.SetValue(transparentDrawPass, transparentFilteringSettings);

            ProPixelizerUtils.CalculateLowResViewProjection(
                cameraData.GetViewMatrix(),
                cameraData.GetProjectionMatrix(),
                cameraData.camera.orthographic,
                cameraData.camera.nearClipPlane,
                cameraData.camera.farClipPlane,
                lowResConfig.OrthoLowResWidth,
                lowResConfig.OrthoLowResHeight,
                lowResConfig.LowResCameraDeltaWS,
                lowResConfig.LowResPerspectiveVerticalFOV,
                lowResConfig.LowResAspect,
                Settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera,
                out var viewMatrix,
                out var projectionMatrix
                );

            using (var builder = renderGraph.AddUnsafePass<PassData>(GetType().Name, out var passData))
            {
                passData.settings = Settings;
                passData.LowResColor = lowRes.Color;
                passData.LowResDepth = lowRes.Depth;
                passData.preview = cameraData.cameraType == CameraType.Preview;
                passData.clearColor = cameraData.camera.backgroundColor;
                passData.baseCamera = cameraData.camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base;
                passData.view = viewMatrix;
                passData.projection = projectionMatrix;
                passData.LowresTargetWidth = lowResConfig.Width;
                passData.LowresTargetHeight = lowResConfig.Height;
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.UseTexture(passData.LowResDepth, AccessFlags.Write);
                builder.UseTexture(passData.LowResColor, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    ExecutePass(context.cmd, data);
                });
            }
        }


        void ExecutePass(IUnsafeCommandBuffer command, PassData data)
        {
            if (data.preview)
            {
                command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);
                command.SetRenderTarget(data.LowResColor, data.LowResDepth);
                command.ClearRenderTarget(true, true, data.clearColor);
                return;
            }

            if (data.settings.Filter == PixelizationFilter.FullScene)
            {
                //command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);
                command.SetRenderTarget(data.LowResColor, data.LowResDepth);
                if (data.baseCamera)
                    command.ClearRenderTarget(true, true, data.clearColor);
                else
                    command.ClearRenderTarget(true, false, data.clearColor);
                command.SetViewProjectionMatrices(data.view, data.projection);
                command.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW_INVERSE, data.view.inverse);
                command.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION_INVERSE, data.projection.inverse);
                command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);
                // Required for Forward+ to work; see #255
                float width = data.LowresTargetWidth;
                float height = data.LowresTargetHeight;
                command.SetGlobalVector("_ScaledScreenParams", new Vector4(width, height, 1.0f + 1.0f / width, 1.0f + 1.0f / height));
            }
            else
                ProPixelizerUtils.SetPixelGlobalScale(command, 0.01f); // disable pixel size in other cases - draw objects wont be matching metadata buffer.
        }
    }

    public class ProPixelizerRenderGraphUnRedirectPass : ProPixelizerPass
    {
        public ProPixelizerRenderGraphUnRedirectPass(ShaderResources resources)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            Materials = new MaterialLibrary(resources);
        }

        public MaterialLibrary Materials;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resources = frameData.Get<UniversalResourceData>();
            var lowRes = frameData.Get<LowResTargetData>();
            var originals = frameData.Get<ProPixelizerOriginalCameraTargets>();
            
            if (Settings.Filter == PixelizationFilter.FullScene && cameraData.cameraType != CameraType.Preview)
            {
                // restore original buffers
                resources.cameraColor = originals.Color;
                resources.cameraDepth = originals.Depth;

                using (var builder = renderGraph.AddUnsafePass<PassData>(GetType().Name, out var passData))
                {
                    passData.LowResColor = lowRes.Color;
                    passData.LowResDepth = lowRes.Depth;
                    passData.CameraColor = resources.cameraColor;
                    passData.CameraDepth = resources.cameraDepth;
                    passData.CopyDepth = Materials.CopyDepth;
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                    {
                        ExecutePass(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), data);
                    });
                }
            }
        }

        class PassData
        {
            public TextureHandle CameraColor;
            public TextureHandle CameraDepth;
            public TextureHandle LowResColor;
            public TextureHandle LowResDepth;
            public Material CopyDepth;
        }

        void ExecutePass(CommandBuffer command, PassData data)
        {
            //Blitter.BlitCameraTexture(command, data.LowResColor, data.CameraColor);
            // Only color needs to actually be blitted after the redirection - the depth depth buffer was blitted out after Opaques and before transparents.
            //Blitter.BlitCameraTexture(command, data.LowResDepth, data.CameraDepth, data.CopyDepth, 0);
        }

        /// <summary>
        /// Shader resources used by the PixelizationPass.
        /// </summary>
        [Serializable]
        public sealed class ShaderResources
        {
            public Shader CopyDepth;

            private const string CopyDepthShaderName = "Hidden/ProPixelizer/SRP/BlitCopyDepth";

            public ShaderResources Load()
            {
                CopyDepth = Shader.Find(CopyDepthShaderName);
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

            public MaterialLibrary(ShaderResources resources)
            {
                Resources = resources;
            }
        }
    }
#endif
}