// Copyright Elliot Bentine, 2018-
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// A set of rendering utility functions to help me implement functionality in a Unity Engine Version agnostic way.
    /// </summary>
    public static class ProPixelizerUtils {

        public static void SetCameraMatrices(CommandBuffer cmd, ref CameraData cameraData)
        {
#if CAMERADATA_MATRICES
            cmd.SetViewMatrix(cameraData.GetViewMatrix());
            cmd.SetProjectionMatrix(cameraData.GetProjectionMatrix());
#else
            cmd.SetViewMatrix(cameraData.camera.worldToCameraMatrix);
            cmd.SetProjectionMatrix(cameraData.camera.projectionMatrix);
#endif
        }

#if UNITY_2023_3_OR_NEWER
        public static void SetPixelGlobalScale(IRasterCommandBuffer buffer, float scale)
        {
            buffer.SetGlobalFloat(ProPixelizerFloats.PIXEL_SCALE, scale);
        }
#endif

        public static void SetPixelGlobalScale(CommandBuffer buffer, float scale)
        {
            buffer.SetGlobalFloat(ProPixelizerFloats.PIXEL_SCALE, scale);
        }

        /// <summary>
        /// Calculates view and projection matrices for the low resolution targets.
        /// </summary>
        public static void CalculateLowResViewProjection(
            in Matrix4x4 cameraView, in Matrix4x4 cameraProjection,
            bool ortho, float nearClip, float farClip,
            float OrthoWidth, float OrthoHeight, Vector3 LowResCameraDeltaWS,
            float LowResPerspectiveVerticalFOV, float aspect,
            bool forSubcamera,
            out Matrix4x4 view, out Matrix4x4 projection
            )
        {
            if (ortho)
                projection = Matrix4x4.Ortho(
                    -OrthoWidth / 2f, OrthoWidth / 2f,
                    -OrthoHeight / 2f, OrthoHeight / 2f,
                    nearClip, farClip
                    );
            else
                projection = Matrix4x4.Perspective(LowResPerspectiveVerticalFOV, aspect, nearClip, farClip);
            if (forSubcamera)
                view = cameraView;
            else
                view = cameraView * Matrix4x4.Translate(LowResCameraDeltaWS);
        }

        /// <summary>
        /// Sets view and projection matrices used to render to the low-res target.
        /// </summary>
        public static void SetLowResViewProjectionMatrices(
            CommandBuffer cmd,
            ref CameraData cameraData,
            float OrthoWidth,
            float OrthoHeight,
            Vector3 LowResCameraDeltaWS,
            float LowResPerspectiveVerticalFOV,
            float aspect,
            bool forSubcamera
            )
        {
            var camera = cameraData.camera;
            CalculateLowResViewProjection(
                cameraData.GetViewMatrix(),
                cameraData.GetProjectionMatrix(),
                camera.orthographic,
                camera.nearClipPlane, camera.farClipPlane,
                OrthoWidth, OrthoHeight,
                LowResCameraDeltaWS,
                LowResPerspectiveVerticalFOV, aspect,
                forSubcamera,
                out var viewMatrix, out var projectionMatrix
                );

            cmd.SetViewMatrix(viewMatrix);
            cmd.SetProjectionMatrix(projectionMatrix);
            cmd.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW, viewMatrix);
            cmd.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION, projectionMatrix);
            cmd.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW_INVERSE, viewMatrix.inverse);
            cmd.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION_INVERSE, projectionMatrix.inverse);
        }

        /// <summary>
        /// Size of the margin around the visible low-res pixel target, in pixels.
        /// </summary>
        public const int LOW_RESOLUTION_TARGET_MARGIN = 5;

        /// <summary>
        /// Calculates a low resolution target size
        /// </summary>
        /// <param name="cameraWidth">width of the camera render texture in pixels</param>
        /// <param name="cameraHeight">height of the camera render texture in pixels</param>
        /// <param name="scale">resolution scale of the low res target. Values < 1 are pixelated.</param>
        /// <param name="width">output width in pixels</param>
        /// <param name="height">output height in pixels</param>
        public static void CalculateLowResTargetSize(int cameraWidth, int cameraHeight, float scale, out int width, out int height, bool withMargin = true)
        {
            // see matching define in ScreenUtils.hlsl
            width = Mathf.CeilToInt(cameraWidth * scale);// + 2;
            height = Mathf.CeilToInt(cameraHeight * scale);// + 2;
            //force odd dimensions by /2*2+1
            width = 2 * (width / 2) + 1 + (withMargin ? 2 * LOW_RESOLUTION_TARGET_MARGIN : 0);
            height = 2 * (height / 2) + 1 + (withMargin ? 2 * LOW_RESOLUTION_TARGET_MARGIN : 0);

            width = Mathf.Min(width, cameraWidth + 1 + (withMargin ? 2 * LOW_RESOLUTION_TARGET_MARGIN : 0));
            height = Mathf.Min(height, cameraHeight + 1 + (withMargin ? 2 * LOW_RESOLUTION_TARGET_MARGIN : 0));
        }

#if UNITY_EDITOR
        public static readonly string USER_GUIDE_URL = Universal.ShaderGraph.ProPixelizerURPConstants.USER_GUIDE_URL;
#endif
    }
}