// Copyright Elliot Bentine, 2018-
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// Abstract base for ProPixelizer passes
    /// </summary>
    public abstract class ProPixelizerPass : ScriptableRenderPass
    {
        public ProPixelizerPass()
        {
        }

        public ProPixelizerSettings Settings;

        public void ConfigureForSettings(ProPixelizerSettings settings)
        {
            Settings = settings;
        }

        public void SetColorAndDepthTargets(CommandBuffer buffer, ref RenderingData renderingData)
        {
            // Note that for 2020 and 2021, the cameraDepthTarget only works for the Game tab, and not the Scene tab camera. Instead, you have to
            // blit to the depth element of the ColorTarget, which works for Scene view.
            // HOWEVER, the issue is that this element doesn't exist for the Metal API, and so an error gets thrown. It seems there is no way
            // to blit to the scene tab's depth buffer for Metal on 2020/2021.
            // 
            // https://forum.unity.com/threads/scriptablerenderpasses-cannot-consistently-access-the-depth-buffer.1263044/
            //var device = SystemInfo.graphicsDeviceType;
            //if (device == GraphicsDeviceType.Metal)
            //{
            //	buffer.SetRenderTarget(ColorTarget(ref renderingData), DepthTarget(ref renderingData));
            //}
            //else
            //{
            //	buffer.SetRenderTarget(ColorTarget(ref renderingData)); //use color target depth buffer.
            //}
            //
            // Solution may be below - still to check:
#if UNITY_2022_3_OR_NEWER
#pragma warning disable 0618
            // https://github.com/Unity-Technologies/Graphics/blob/008fceea35df3bd1fd61878ae560d50de04b0b8d/Packages/com.unity.render-pipelines.universal/Runtime/ScriptableRenderer.cs#L2024C17-L2024C80
            if (renderingData.cameraData.renderer.cameraDepthTargetHandle.nameID == BuiltinRenderTextureType.CameraTarget)
                buffer.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle, renderingData.cameraData.renderer.cameraColorTargetHandle); // color both
            else
                buffer.SetRenderTarget(ColorTarget(ref renderingData), DepthTarget(ref renderingData));
#pragma warning restore 0618
#else
            if (renderingData.cameraData.renderer.cameraDepthTarget == BuiltinRenderTextureType.CameraTarget)
                //buffer.SetRenderTarget(ColorTarget(ref renderingData), ColorTarget(ref renderingData)); // color both
                buffer.SetRenderTarget(ColorTarget(ref renderingData)); // color both
            else
                buffer.SetRenderTarget(ColorTarget(ref renderingData), DepthTarget(ref renderingData));
#endif
        }

#pragma warning disable 0618
        public RTHandle ColorTarget(ref RenderingData data) => data.cameraData.renderer.cameraColorTargetHandle;
        public RTHandle DepthTarget(ref RenderingData data) => data.cameraData.renderer.cameraDepthTargetHandle;

        public RTHandle ColorTargetForPixelization(ref RenderingData data, ProPixelizerLowResolutionTargetPass target) =>
            Settings.TargetForPixelation == TargetForPixelation.CameraTarget
            ? ColorTarget(ref data)
            : target.Color;

        public RTHandle DepthTargetForPixelization(ref RenderingData data, ProPixelizerLowResolutionTargetPass target) =>
            Settings.TargetForPixelation == TargetForPixelation.CameraTarget
            ? DepthTarget(ref data)
            : target.Depth;
#pragma warning restore 0618

        private const string PROPIXELIZER_METADATA_LIGHTMODE = "ProPixelizer";

        protected static ShaderTagId ProPixelizerMetadataLightmodeShaderTag = new ShaderTagId(PROPIXELIZER_METADATA_LIGHTMODE);

#if UNITY_2023_3_OR_NEWER
        /// <summary>
        /// Get the camera color texture.
        /// 
        /// Seems daft, but was needed in preview Unity 6 versions to wrap different behavior on preview cameras.
        /// </summary>
        protected static TextureHandle CameraColorTexture(UniversalResourceData resource, UniversalCameraData camera) => resource.cameraColor;
        protected static TextureHandle CameraDepthTexture(UniversalResourceData resource, UniversalCameraData camera) => resource.cameraDepth;
#endif
    }
}