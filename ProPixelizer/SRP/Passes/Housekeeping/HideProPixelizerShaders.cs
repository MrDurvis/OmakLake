// Copyright Elliot Bentine, 2018-
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// Disables ProPixelizer shaders from being drawn.
    /// </summary>
    public class HideProPixelizerShaders : ProPixelizerSetGlobalsPass
    {
        public HideProPixelizerShaders() : base()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

#if UNITY_2023_3_OR_NEWER
        public override void ExecuteCommands(RasterCommandBuffer buffer)
#else
        public override void ExecuteCommands(CommandBuffer buffer)
#endif
        {
            buffer.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 0.0f);
        }
    }
}