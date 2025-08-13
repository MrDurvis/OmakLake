// Copyright Elliot Bentine, 2018-
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
    /// <summary>
    /// Provides a wrapper for setting parameters in a specific render pass event.
    /// </summary>
    public abstract class ProPixelizerSetGlobalsPass : ProPixelizerPass
    {
        public ProPixelizerSetGlobalsPass() : base()
        {
        }

#if UNITY_2023_3_OR_NEWER
        public virtual void ExecuteCommands(RasterCommandBuffer buffer)
#else
        public virtual void ExecuteCommands(CommandBuffer buffer)
#endif
        {

        }

        #region non-RG

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer buffer = CommandBufferPool.Get(GetType().Name);
#if UNITY_2023_3_OR_NEWER
            ExecuteCommands(CommandBufferHelpers.GetRasterCommandBuffer(buffer));
#else
            ExecuteCommands(buffer);
#endif
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }

#endregion

        #region RG
#if UNITY_2023_3_OR_NEWER
        class PassData
        {
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetType().Name, out var passData))
            {
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    ExecuteCommands(context.cmd);
                });
            }
        }
#endif
#endregion
    }
}