// Copyright Elliot Bentine, 2018-
using System;
using UnityEngine;

namespace ProPixelizer
{
    public struct ProPixelizerSettings
    {
        public PixelizationFilter Filter;
        public LayerMask PixelizedLayers;
        public bool UsePixelExpansion;
        /// <summary>
        /// The resolution scaling of the low-resolution target, relative to the screen. Lower values = greater pixelisation.
        /// </summary>
        public float DefaultLowResScale;
        public bool UseDepthTestingForEdgeOutlines;
        public bool UseNormalsForEdgeDetection;
        public float DepthTestThreshold;
        public bool UseDepthTestingForIDOutlines;
        public float NormalEdgeDetectionThreshold;
        public Texture2D FullscreenLUT;
        public bool UsePixelArtUpscalingFilter;
        public bool Enabled;
        public ProPixelizerRenderingPathway RenderingPathway;
        public RecompositionLowResSource RecompositionSource =>
            RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera || RenderingPathway == ProPixelizerRenderingPathway.SubCamera
                ? RecompositionLowResSource.ProPixelizerColorTarget
                : RecompositionLowResSource.SubCamera;
        public RecompositionMode RecompositionMode
        {
            get
            {
                if (RenderingPathway == ProPixelizerRenderingPathway.SubCamera)
                    return RecompositionMode.None;
                if (Filter == PixelizationFilter.FullScene || RenderingPathway == ProPixelizerRenderingPathway.MainCamera)
                    return RecompositionMode.UseLowRes;
                return RecompositionMode.DepthBasedMerge;
            }
        }

        /// <summary>
        /// The target into which ProPixelizer shaders are rendered and pixel expanded.
        /// </summary>
        public TargetForPixelation TargetForPixelation => 
            RenderingPathway == ProPixelizerRenderingPathway.SubCamera
                ? TargetForPixelation.CameraTarget
                : TargetForPixelation.ProPixelizerTarget;
    }

    public enum PixelizationFilter
    {
        OnlyProPixelizer,
        FullScene,
        [InspectorName(null)] Layers
    }

    public enum ProPixelizerRenderingPathway
    {
        MainCamera,
        SubCamera,
        MainWithVirtualCamera
    }

    /// <summary>
    /// ProPixelizer's approach for merging a low-resolution target back into the screen.
    /// </summary>
    public enum RecompositionMode
    {
        /// <summary>
        /// No recomposition, everything should be done directly into scene.
        /// </summary>
        None,
        /// <summary>
        /// Use the low resolution target in entirety.
        /// </summary>
        UseLowRes,
        /// <summary>
        /// Low-res target is merged based on comparisons of depth buffer.
        /// </summary>
        DepthBasedMerge
    }

    /// <summary>
    /// The source used for recompositing the low resolution scene.
    /// </summary>
    public enum RecompositionLowResSource
    {
        ProPixelizerColorTarget,
        SubCamera
    }

    public enum TargetForPixelation
    {
        CameraTarget,
        ProPixelizerTarget
    }
}