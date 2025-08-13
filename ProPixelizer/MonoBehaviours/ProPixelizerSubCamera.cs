// Copyright Elliot Bentine, 2018-
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// ProPixelizer's subcamera is used for rendering a low-resolution view of the scene.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class ProPixelizerSubCamera : MonoBehaviour
    {
        Camera _camera;
        RTHandle _Color;
        RTHandle _Depth;
        ProPixelizerCamera _proPixelizerCamera;

        public ProPixelizerCamera MainCamera => _proPixelizerCamera;

        public RTHandle Color { get { return _Color; } }
        public RTHandle Depth { get { return _Depth; } }

        private void Initialise()
        {
            _camera = GetComponent<Camera>();
            Configure();
        }

        public void Update()
        {
            // Automatically disable subcamera when not required.
            if (MainCamera == null || !MainCamera.isActiveAndEnabled || !MainCamera.OperatingAsMainCamera)
            {
                gameObject.SetActive(false);
            }
        }

        public void Configure()
        {
            _proPixelizerCamera = transform.parent.GetComponent<ProPixelizerCamera>();
            if (_proPixelizerCamera == null)
            {
                Debug.LogError("A ProPixelizerSubCamera without a main camera has been detected. The ProPixelizerSubCamera MonoBehavior should be attached to a camera, which is a child of the main camera with a ProPixelizerCamera MonoBehavior.");
                return;
            }
            _proPixelizerCamera.SubCamera = this;
        }

        public void MirrorMainCameraParameters()
        {
            var parentCamera = transform.parent.GetComponent<Camera>();
            if (parentCamera != null)
            {
                _camera.depth = parentCamera.depth - 1;
                _camera.orthographic = parentCamera.orthographic;
                _camera.nearClipPlane = parentCamera.nearClipPlane;
                _camera.farClipPlane = parentCamera.farClipPlane;
                _camera.cullingMask = parentCamera.cullingMask & (int.MaxValue ^ _proPixelizerCamera.SubcameraExcludedLayers.value);

                var parentData = parentCamera.GetUniversalAdditionalCameraData();
                var data = _camera.GetUniversalAdditionalCameraData();
                data.renderShadows = parentData.renderShadows;
                data.dithering = parentData.dithering;
            }
        }

        public void OnEnable()
        {
            Initialise();
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
#pragma warning disable 0618
            RenderPipelineManager.beginFrameRendering += BeginFrameRendering;
            RenderPipelineManager.endFrameRendering += EndFrame;
#pragma warning restore 0618
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            RenderPipelineManager.endContextRendering += EndContext;

        }

        public void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
#pragma warning disable 0618
            RenderPipelineManager.beginFrameRendering -= BeginFrameRendering;
            RenderPipelineManager.endFrameRendering -= EndFrame;
#pragma warning restore 0618
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RenderPipelineManager.endContextRendering -= EndContext;

            _camera.targetTexture = null;
            _Color?.Release();
            _Depth?.Release();
        }

        public void ModifySettings(ref ProPixelizerSettings settings)
        {
            if (MainCamera == null) return;
            MainCamera.ModifySettings(ref settings);
            settings.RenderingPathway = ProPixelizerRenderingPathway.SubCamera;
        }

        private float _cachedRenderScale;

        public void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera)
                return;

            // perform snapping of associated ProPixelizer camera
            _proPixelizerCamera.Snap();

            // For an orthographic camera, adjust parameters to match the low-resolution target settings.
            if (camera.orthographic)
            {
                camera.orthographicSize = _proPixelizerCamera.OrthoLowResHeight / 2f;
                transform.position = transform.parent.position - _proPixelizerCamera.LowResCameraDeltaWS;
                _camera.ResetWorldToCameraMatrix();
            }

            // camera.targetTexture = null;
            var format = camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
            var colorDescriptor = new RenderTextureDescriptor(
                _proPixelizerCamera.LowResTargetWidth, _proPixelizerCamera.LowResTargetHeight, format, 0);

#if UNITY_6000_0_OR_NEWER
            colorDescriptor.depthBufferBits = 32;
            var renderTexture = RenderTexture.GetTemporary(colorDescriptor);
            renderTexture.filterMode = FilterMode.Bilinear;
            renderTexture.wrapMode = TextureWrapMode.Clamp;
            renderTexture.autoGenerateMips = false;
            // From documentation:
            // Keep in mind that render texture contents can become "lost" on certain events, like loading a new level,
            // system going to a screensaver mode, in and out of fullscreen and so on. When that happens, your existing
            // render textures will become "not yet created" again, you can check for that with IsCreated function.

            _Color = RTHandles.Alloc(renderTexture);
            
            var depthDescriptor = colorDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
            depthDescriptor.depthBufferBits = 32;
            _Depth = RTHandles.Alloc(
                colorDescriptor.width,
                colorDescriptor.height,
                depthBufferBits: DepthBits.Depth32,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                autoGenerateMips: false,
                colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.None,
                name: "SubCameraDepth"
                );

            camera.targetTexture = renderTexture;
#else
            var colorRenderTexture = RenderingUtils.ReAllocateIfNeeded(
                ref _Color, in colorDescriptor,
                name: "SubCameraColor",
                filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp
                );
            var depthDescriptor = colorDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
            depthDescriptor.depthBufferBits = 32;
            RenderingUtils.ReAllocateIfNeeded(
                ref _Depth, in depthDescriptor,
                name: "SubCameraDepth",
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp
                );
            camera.targetTexture = _Color;
            
            // This is infuriating, but there is no way to create a RTHandle with depth buffer.
            // That doesn't matter, because I have both _Color and _Depth right? Wrong!
            // - RenderGraph demands a RenderTexture with depth as camera output, or it throws warnings.
            // - The DBuffer Decal render feature will churn out warnings due to a bug that doesn't account for cameraTargetDescriptor with 0 bit depth.
            // See ProPixelizer Issue #246
            _Color.rt.Release();
            _Color.rt.depth = 32;
            _Color.rt.Create();
#endif
            // Renderscale must be 1 for subcameras - we manually set the texture size according to target world space pixel size.
            _cachedRenderScale = ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale;
            ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale = 1f;
        }

        public void BeginFrameRendering(ScriptableRenderContext context, Camera[] cameras)
        {
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorTexture = false;
            data.requiresDepthTexture = false;
            
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            
            MirrorMainCameraParameters();
        }

        public void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera)
                return;

            // unsnap associated ProPixelizer camera
            _proPixelizerCamera.Release();
            ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale = _cachedRenderScale;
        }

        public void EndContext(ScriptableRenderContext context, List<Camera> camera)
        {
            CleanupSubcameraResources();
        }

        public void EndFrame(ScriptableRenderContext context, Camera[] camera)
        {
            CleanupSubcameraResources();
        }

        public void CleanupSubcameraResources()
        {
#if UNITY_6000_0_OR_NEWER
            var renderTexture = _camera.targetTexture;
            if (renderTexture == null) return;
#endif
            _camera.targetTexture = null;
#if UNITY_6000_0_OR_NEWER
            renderTexture.Release();
            RTHandles.Release(_Depth);
#endif
        }
    }
}