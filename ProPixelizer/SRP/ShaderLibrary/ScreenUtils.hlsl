// Copyright Elliot Bentine, 2018-
// Helper functions for screen parameters.
// Some of these parameters have not yet been exposed as Shader Graph nodes, so this file is needed.

#ifndef SCREEN_UTIL_INCLUDED
#define SCREEN_UTIL_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Version.hlsl"
#include "ShaderGraphUtils.hlsl"

float4 _ProPixelizer_RenderTargetInfo;

void GetScaledScreenParameters_float(out float4 Out)
{
	#if SHADERGRAPH_PREVIEW_TEST
		Out = float4(0, 0, 0, 0);
	#else
	Out = _ProPixelizer_RenderTargetInfo;
	#endif
}

float4 _ProPixelizer_ScreenTargetInfo;

float GetProPixelizerLowResTargetMargin() {
	return 5.0;
}

void GetScreenTargetParameters_float(out float4 Out)
{
#if SHADERGRAPH_PREVIEW_TEST
	Out = float4(0, 0, 0, 0);
#else
	Out = _ProPixelizer_ScreenTargetInfo;
#endif

	// Note that there are Unity properties for this, e.g. _ScaledScreenParams,
	// and functions to use them, see e.g.
	// https://github.com/Unity-Technologies/Graphics/blob/632f80e011f18ea537ee6e2f0be3ff4f4dea6a11/Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/ShaderVariablesFunctions.hlsl
	// However, also note that I have found them to be unreliable: https://forum.unity.com/threads/_scaledscreenparameters-and-render-target-subregion.1336277/
}

float4 _ProPixelizer_LowResCameraDeltaUV;
float4 _ProPixelizer_OrthoSizes;

inline float2 GetLowResCameraDeltaUV() {
	return float2(_ProPixelizer_LowResCameraDeltaUV.xy);
}

inline float2 ConvertToLowResolutionTargetUV(float2 uv) {
	if (unity_OrthoParams.w > 0.5) { 
		// orthographic - adjust for lowres and normal ortho sizes.
		float2 lowResDeltaUV = GetLowResCameraDeltaUV();
		return float2(
			lowResDeltaUV.x + 0.5 + (uv.x - 0.5) * _ProPixelizer_OrthoSizes.z / _ProPixelizer_OrthoSizes.x,
			lowResDeltaUV.y + 0.5 + (uv.y - 0.5) * _ProPixelizer_OrthoSizes.w / _ProPixelizer_OrthoSizes.y
			);
	} else {
		// perspective - adjust for lowres FOV and margins.
		return float2(0.5, 0.5) + (uv - float2(0.5, 0.5)) * float2(
			(_ProPixelizer_RenderTargetInfo.x - 2 * GetProPixelizerLowResTargetMargin()) / _ProPixelizer_RenderTargetInfo.x,
			(_ProPixelizer_RenderTargetInfo.y - 2 * GetProPixelizerLowResTargetMargin()) / _ProPixelizer_RenderTargetInfo.y
			);
	}
}
#endif
