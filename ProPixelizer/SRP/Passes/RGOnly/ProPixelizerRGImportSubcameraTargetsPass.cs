// Copyright Elliot Bentine, 2018-
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
    /// Imports the color and depth targets from the previously executed subcamera into the main camera rendergraph.
    /// 
    /// (RG only)
    /// </summary>
    public class ProPixelizerRGImportSubcameraTargetsPass : ProPixelizerPass
	{
		public ProPixelizerRGImportSubcameraTargetsPass()
		{
			renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
		}

        // (No non-RG pathway)
		#pragma warning disable 0672
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
        }
		#pragma warning restore 0672

        #region RG
#if UNITY_2023_3_OR_NEWER

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			var cameraData = frameData.Get<UniversalCameraData>();
			var propixelizerCamera = cameraData.camera.GetComponent<ProPixelizerCamera>();
			if (propixelizerCamera == null || propixelizerCamera.SubCamera == null)
				return;
			var subcameraTargets = frameData.GetOrCreate<ProPixelizerSubCameraTargets>();
            subcameraTargets.Depth = renderGraph.ImportBackbuffer(propixelizerCamera.SubCamera.Depth);
            subcameraTargets.Color = renderGraph.ImportBackbuffer(propixelizerCamera.SubCamera.Color);
        }
#endif
        #endregion
    }
}