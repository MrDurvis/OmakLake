// Copyright Elliot Bentine, 2018-
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// Disables ProPixelizer shaders from being drawn..
    /// </summary>
    public class SetProPixelizerPixelScaleForPrepass : ProPixelizerSetGlobalsPass
    {
        public SetProPixelizerPixelScaleForPrepass() : base()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
        }

#if UNITY_2023_3_OR_NEWER
        public override void ExecuteCommands(RasterCommandBuffer buffer)
#else
        public override void ExecuteCommands(CommandBuffer buffer)
#endif
        {
            if (Settings.UsePixelExpansion)
                ProPixelizerUtils.SetPixelGlobalScale(buffer, 1f);
            else
                ProPixelizerUtils.SetPixelGlobalScale(buffer, 0.01f);
        }
    }
}