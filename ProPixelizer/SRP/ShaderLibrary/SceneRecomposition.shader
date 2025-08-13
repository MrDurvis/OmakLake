// Copyright Elliot Bentine, 2018-
// 
// Part of the ProPixelizer Package.
// 
// This shader recomposits a low-resolution target back into the high resolution screen target. The recomposition
// accounts for dissimilarity between the low-res and main scene camera projection, allowing smooth sub-pixel motion
// of the camera.
// 
// # Recomposition modes
// 
// The following recomposition modes are available:
// - RECOMPOSITION_DEPTH_BASED: The primary texture is blended with the secondary texture by comparing the depth
//                              buffers and selecting the nearest of the two.
// - RECOMPOSITION_ONLY_SECONDARY: Only the secondary target is used when recompositing the scene.
//
// The local keyword DEPTH_OUTPUT_ON can be used to enable or disable depth output.
// - All modes: accounts for dissimilarity between the low-res and main scene camera projection,
//     which allows smooth sub-(low-res) pixel camera movement at the screen resolution.


Shader "Hidden/ProPixelizer/SRP/Internal/SceneRecomposition" {
	SubShader {
		Pass {
			ZTest Always Cull Off ZWrite On

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0
			#pragma multi_compile DEPTH_OUTPUT_ON _
			#pragma multi_compile RECOMPOSITION_DEPTH_BASED RECOMPOSITION_ONLY_SECONDARY
			#pragma multi_compile PIXELART_AA_FILTER_ON _

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

			// The main texture input.
			// ( for definitions see:
			//    Graphics/Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blitter.cs
			//    Blitter.BlitShaderIDs
			// )
			// sampler2D _BlitTexture;
			TEXTURE2D_X_FLOAT(_InputDepthTexture);
			SAMPLER(sampler_InputDepthTexture_point_clamp);

			TEXTURE2D_X_FLOAT(_SecondaryTexture);
			#if PIXELART_AA_FILTER_ON
				#define PIXELART_SAMPLER sampler_linear_clamp
			#else
				#define PIXELART_SAMPLER sampler_point_clamp
			#endif
			SAMPLER(PIXELART_SAMPLER);
			TEXTURE2D_X_FLOAT(_SecondaryDepthTexture);
			SAMPLER(sampler_SecondaryDepthTexture_point_clamp);

			struct DBCVaryings {
				float4 vertex : SV_POSITION;
				float2 texcoord : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			DBCVaryings vert(Attributes v) {
				Varyings vars;
				vars = Vert(v);
				DBCVaryings o;
				o.vertex = vars.positionCS;
				o.texcoord = vars.texcoord;
				return o;
			}

			#include "ScreenUtils.hlsl"

#if DEPTH_OUTPUT_ON
			void frag(DBCVaryings i, out float4 outColor : SV_Target, out float outDepth : SV_Depth) {
#else
			void frag(DBCVaryings i, out float4 outColor : SV_Target) {
				float outDepth;
#endif
				float baseDepth;
				float secondaryDepth;

				float lowResolutionTargetMargin = GetProPixelizerLowResTargetMargin(); // defined in ProPixelizerUtils.cs
				float2 scaledCoord = ConvertToLowResolutionTargetUV(i.texcoord);

				#if RECOMPOSITION_ONLY_SECONDARY && !SHADERGRAPH_PREVIEW_TEST

				// When using the secondary target alone, we can use a pixel art AA filter.
				// 		Based on the work of Cole Cecil, see here: 
				//        https://colececil.io/blog/2017/scaling-pixel-art-without-destroying-it/
				// 
				// This cannot be used for hybrid mode, see eg:
				//     	https://github.com/ProPixelizer/ProPixelizer/issues/180.
					
				float2 pixels_per_texel = _ProPixelizer_ScreenTargetInfo.xy / _ProPixelizer_RenderTargetInfo.xy;

				// There are two cases that occur when upscaling:
				//   1. A pixel is entirely within a texel. In this case, we just take the texel color (sample at texel centre).
				//   2. A pixel partially covers multiple texels. In this case, we take a weighted sample of the surrounding texels.
				// Both can be elegantly described by taking a transfer function for the low_res_coordinate:
				// 
				// ^  (uv)
				// |           ---
				// |          /
				// |      ----
				// |     /
				// |  ---
				// x--------------> (in)
				// 
				// This blends the uvs between texels for intermediate pixels, and snaps to texel centres for completely contained pixels.
				// The width of an 'edge' `/` in texel space is 'texels_per_pixel' / _RenderTargetInfo.xy. 
				// 
				// (Note that because we are only blitting two rectangles together, of same orientation, we don't need
				// to consider rotational effects that are required more generally).
				float2 low_res_coordinate = scaledCoord * _ProPixelizer_RenderTargetInfo.xy; // n+0.5 on texel centres.
				float2 low_res_texel_frac = frac(low_res_coordinate);
				float2 low_res_texel_base = floor(low_res_coordinate);
				float2 low_res_texel_offset = clamp(low_res_texel_frac * pixels_per_texel, 0, 0.5)
					+ clamp((low_res_texel_frac - 1) * pixels_per_texel + 0.5, 0.0, 0.5);
				scaledCoord = (low_res_texel_base + low_res_texel_offset) / _ProPixelizer_RenderTargetInfo.xy;

				#endif

				secondaryDepth = SAMPLE_TEXTURE2D_X(_SecondaryDepthTexture, sampler_SecondaryDepthTexture_point_clamp, scaledCoord).r;
				#if RECOMPOSITION_DEPTH_BASED
					baseDepth = SAMPLE_TEXTURE2D_X(_InputDepthTexture, sampler_InputDepthTexture_point_clamp, i.texcoord).r;
					#if UNITY_REVERSED_Z
						float delta = baseDepth - secondaryDepth;
					#else
						float delta = secondaryDepth - baseDepth;
					#endif
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				#endif

				#if RECOMPOSITION_DEPTH_BASED
					if (delta >= 0) {
						outDepth = baseDepth;
						outColor = SAMPLE_TEXTURE2D_X(_BlitTexture, PIXELART_SAMPLER, i.texcoord);
					} else {
						outDepth = secondaryDepth;
						outColor = SAMPLE_TEXTURE2D_X(_SecondaryTexture, PIXELART_SAMPLER, scaledCoord);
					}
				#elif RECOMPOSITION_ONLY_SECONDARY
					outDepth = secondaryDepth;
					outColor = SAMPLE_TEXTURE2D_X(_SecondaryTexture, PIXELART_SAMPLER, scaledCoord);
				#endif
			}
			ENDHLSL
		}
	}
	//Fallback "Hidden/Universal Render Pipeline/Blit"
}