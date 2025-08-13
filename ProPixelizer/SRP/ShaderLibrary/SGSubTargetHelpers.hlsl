// Copyright Elliot Bentine 2018-

// Helper functions for the ShaderGraph SubTarget.

#ifndef PROPIXELIZERSGHELPERS_INCLUDED
#define PROPIXELIZERSGHELPERS_INCLUDED

#include "ScreenUtils.hlsl"
#include "ShaderGraphUtils.hlsl"

inline void ProPixelizerHelpers_float(float3 ObjectPosition_WS, float4 ScreenPosition_Raw, out float3 ObjectCentre_WS, out float4 posCS, out float PixelSize) {

#if !defined(PROPIXELIZER_SUBTARGET)
	//|| SHADERPASS == SHADERPASS_PREVIEW
	ObjectCentre_WS = ObjectPosition_WS;
#else
	#if defined(USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON)
		ObjectCentre_WS = ObjectPosition_WS;
	#else
		ObjectCentre_WS = _PixelGridOrigin.xyz;
	#endif
#endif

	float4 screenParams;
	GetScaledScreenParameters_float(screenParams);
	posCS = float4(ScreenPosition_Raw.rg / ScreenPosition_Raw.a * screenParams.rg, 0, 0);

#if !defined(PROPIXELIZER_SUBTARGET)
	//SHADERGRAPH_PREVIEW_WINDOW || SHADERGRAPH_PREVIEW_TEST
	PixelSize = 5;
#else
	PixelSize = _PixelSize;
#endif
}

#endif