// Copyright Elliot Bentine, 2018-
Shader "Hidden/ProPixelizer/SRP/OutlineDetection" {
	Properties{
		_OutlineDepthTestThreshold("Threshold used for depth testing outlines.", Float) = 0.0001
	}

		SubShader{
		Tags{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Opaque"
			"PreviewType" = "Plane"
		}

		Pass{
			Cull Off
			ZWrite On
			ZTest Off

			HLSLINCLUDE
				#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
				//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Common.hlsl" // LinearEyeDepth
				#include "PixelUtils.hlsl"
				#include "PackingUtils.hlsl"
				#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"		
			ENDHLSL

			HLSLPROGRAM
			#pragma target 2.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_local DEPTH_TEST_OUTLINES_ON _
			#pragma multi_compile NORMAL_EDGE_DETECTION_ON _
			#pragma multi_compile_local DEPTH_TEST_NORMAL_EDGES_ON _

			#if DEPTH_TEST_OUTLINES_ON
			float _OutlineDepthTestThreshold;
			#endif

			#if NORMAL_EDGE_DETECTION_ON
			float _NormalEdgeDetectionSensitivity;
			#endif

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex_point_clamp);
			float4 _TexelSize;
			TEXTURE2D_X_FLOAT(_MainTex_Depth);
			SAMPLER(sampler_MainTex_Depth_point_clamp);

			float4x4 _ProPixelizer_LowRes_I_V;
			float4x4 _ProPixelizer_LowRes_I_P;

			struct ProPVaryings {
				float4 pos : SV_POSITION;
				float4 scrPos : TEXCOORD1;
			};

			#define BLIT_API UNITY_VERSION >= 202220
			#define DEPTH_USED DEPTH_TEST_OUTLINES_ON || DEPTH_TEST_NORMAL_EDGES_ON || NORMAL_EDGE_DETECTION_ON
			// 2022.2 & URP14+
			#if BLIT_API
				#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

				ProPVaryings vert(Attributes v) {
					Varyings vars;
					vars = Vert(v);
					ProPVaryings o;
					o.pos = vars.positionCS;
					o.scrPos = float4(ComputeNormalizedDeviceCoordinatesWithZ(o.pos.xyz).xyz, 0);
					return o;
				}
			#else
				struct Attributes
				{
					float4 vertex   : POSITION;  // The vertex position in model space.
					float3 normal   : NORMAL;    // The vertex normal in model space.
					float4 texcoord : TEXCOORD0; // The first UV coordinate.
				};

				ProPVaryings vert(Attributes v) {
					ProPVaryings o;
					o.pos = TransformObjectToHClip(v.vertex.xyz);
					o.scrPos = float4(ComputeNormalizedDeviceCoordinatesWithZ(o.pos.xyz).xyz, 0);
					return o;
				}
			#endif

			// WILL BE REMOVED: No longer used. However, let's me compare old/new kernels easily for now.
			/// <summary>
			/// Tests the outline IDs are the same for the given gradient dir. This is to prevent 'creasing' when objects differ (for which silhouette is more reliable). 
			/// </summary>
			inline float checkMatchingIDsForCrease(float2 mainTexel, float2 gradientDir, float ID, float pixelSize) {
				
				float4 neighbourA = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex_point_clamp, mainTexel + gradientDir * _TexelSize.xy * pixelSize);
				float4 neighbourB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex_point_clamp, mainTexel - gradientDir * _TexelSize.xy * pixelSize);
				return getUID(neighbourA) == getUID(neighbourB) && getUID(neighbourA) == ID ? 1.0 : 0.0;
			}

			inline float getEyeDepth(float depthRaw) {
				if (unity_OrthoParams.w > 0.5) { // ortho
					#if defined(UNITY_REVERSED_Z)
						return lerp(_ProjectionParams.y, _ProjectionParams.z, depthRaw);
					#else
						return lerp(_ProjectionParams.z, _ProjectionParams.y, depthRaw);
					#endif
				}
				else { 
					return LinearEyeDepth(depthRaw, _ZBufferParams);
				}
			}

			/// <summary>
			/// Encapsulates all information about a neighbour in the low-res target.
			/// 
			/// Zero/low cost fields are always declared and filled.
			/// Expensive fields (samples, calculations) are only declared/filled when keywords are met.
			/// </summary>
			struct Neighbour {
				// ID for ID-based outlines
				float ID;

				// UV coordinate of this neighbour in the target.
				float2 texel;

				// Integer pixel size
				float pixelSize;
				
				// Normal in view space
				float3 normalVS;

				// Offset to neighbour in clip space
				float2 deltaCS;

				#if DEPTH_TEST_NORMAL_EDGES_ON || NORMAL_EDGE_DETECTION_ON
				// Normal in world space
				float3 normalWS;
				#endif

				#if DEPTH_USED
				// Raw depth as sampled from the depth buffer
				float depth;
				#endif

				#if DEPTH_TEST_NORMAL_EDGES_ON
				// View space depth in world units
				float depthVS;
				#endif

				#if DEPTH_TEST_NORMAL_EDGES_ON
				// The world space position. Only populated for 'us'.
				float3 positionWS;
				#endif
			};

			inline Neighbour buildNeighbour(float2 texel, float2 deltaCS, float4 packed) {
				Neighbour nbr = (Neighbour)0;
				nbr.deltaCS = deltaCS;
				nbr.texel = texel;
				UnpackMetadata(packed, nbr.normalVS, nbr.ID, nbr.pixelSize);
				#if DEPTH_USED
					nbr.depth = SAMPLE_TEXTURE2D_X(_MainTex_Depth, sampler_MainTex_Depth_point_clamp, texel).r;
				#endif
				#if	DEPTH_TEST_NORMAL_EDGES_ON || NORMAL_EDGE_DETECTION_ON
					nbr.normalWS = mul(_ProPixelizer_LowRes_I_V, float4(nbr.normalVS.rgb, 0)).xyz;
				#endif
				#if DEPTH_TEST_NORMAL_EDGES_ON
					nbr.depthVS = getEyeDepth(nbr.depth);
				#endif
				return nbr;
			}

			/// <summary>
			/// Samples packed data and depth data for the pixel located 'neighbour' pixels from the main texel.
			/// </summary>
			inline Neighbour sampleNeighbour(float2 mainTexel, float2 neighbour, float pixelSize) {
				
				float2 deltaCS = float2(neighbour.x * _TexelSize.x * pixelSize, neighbour.y * _TexelSize.y * pixelSize);
				float2 texel = mainTexel + deltaCS;
				float4 packed = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex_point_clamp, texel);
				return buildNeighbour(texel, deltaCS, packed);
			}

			/// <summary>
			/// Perform an ID comparison between two Neighbours.
			/// </summary>
			inline float compareIDs(Neighbour us, Neighbour nbr) {
				#if DEPTH_TEST_OUTLINES_ON
					#if UNITY_REVERSED_Z
						bool neighbourInFront = nbr.depth > us.depth + _OutlineDepthTestThreshold;
					#else
						bool neighbourInFront = nbr.depth < us.depth - _OutlineDepthTestThreshold;
					#endif
					return neighbourInFront || (nbr.ID == us.ID && nbr.pixelSize > 0.5) ? 1 : 0;
				#else
					return nbr.ID == us.ID && nbr.pixelSize > 0.5 ? 1 : 0;
				#endif
			}

			/// <summary>
			/// Gets the world space position for a given screen UV and raw depth.
			/// </summary>
			inline float3 getWorldSpacePosition(float2 texel, float depthRaw) {
				float4 posCS = float4(texel * 2 - 1, 1, 1); // xy in range (-1, 1) across screen
				float4 temp = mul(_ProPixelizer_LowRes_I_P, posCS);
				float4 posVS;
				float depthES = getEyeDepth(depthRaw);
				if (unity_OrthoParams.w > 0.5) {// ortho
					posVS = float4(temp.xy, depthES, 1);
				}
				else {
					posVS = float4(depthES * temp.xyz, 1);
				}
				return mul(_ProPixelizer_LowRes_I_V, posVS).xyz;
			}

#if NORMAL_EDGE_DETECTION_ON
			/// <summary>
			/// Calculates if there is an edge present due to normal folds.
			/// 
			/// Not that the output is _inverted_ to enable multiplicative comparison.
			/// </summary>
			inline float edgeTest(Neighbour us, Neighbour nbr, inout float3 average) {
				float3 favoredDirection = float3(0.1, 0.1, -0.99);
				// convert vectors to world space and then dot with favored dir.
				float nbrF = dot(nbr.normalWS, favoredDirection);
				float usF = dot(us.normalWS, favoredDirection);
				float weAreFavored = step(nbrF, usF);
				float sameID = nbr.ID - us.ID < 0.001;
				float nbrPixelated = step(0.5, nbr.pixelSize); // 1 if pixelSize > 0.5

				// perform the comparison
				float similarity = dot(normalize(us.normalVS), normalize(nbr.normalVS));
				float comparison = step(similarity, (1 / _NormalEdgeDetectionSensitivity));
				float edge = step(0.5, comparison * weAreFavored + nbrPixelated); // 1 where normal edges are detected to be drawn.

				float farInFrontOfNeighbour = 0;
				float farBehindNeighbour = 0;
				#if DEPTH_TEST_NORMAL_EDGES_ON
					float3 usWS = us.positionWS;
					// world space position of neighbour, if it were at the same depth as us.
					float3 flatNbrWS = getWorldSpacePosition(nbr.texel, us.depth); 
					float nbrDeltaWSLength = length(flatNbrWS - usWS);
					float3 nbrDeltaNorm = float3(normalize(nbr.deltaCS), 0);
					float normalPerpComponent = dot(nbrDeltaNorm, nbr.normalVS); // sign tells us if we expect neighbour to be nearer or further away.
					// Estimate a range of possible Depth Deltas that we could expect.
					float npcMin = normalPerpComponent - 0.2; // 0.2 for ortho
					float npcMax = normalPerpComponent + 0.2; // 0.2 for ortho
					float expectedDDA = nbrDeltaWSLength * npcMin / -nbr.normalVS.z;
					float expectedDDB = nbrDeltaWSLength * npcMax / -nbr.normalVS.z;
					float actualDepthDelta = nbr.depthVS - us.depthVS;

					#if UNITY_REVERSED_Z
						float depthSign = 1;
					#else
						float depthSign = 1;
					#endif

					// note that the depth comparisons are in scene units!
					float signedDepthDelta = actualDepthDelta * depthSign;
					if (unity_OrthoParams.w > 0.5) { // ortho
						if (signedDepthDelta < 0 && signedDepthDelta < min(min(expectedDDA, expectedDDB), -0.25))
							farInFrontOfNeighbour = 1;
						if (signedDepthDelta > 0 && signedDepthDelta > max(3.0*max(expectedDDA, expectedDDB), 0.25))
							farBehindNeighbour = 1;
					}
					else { // perspective
						if (signedDepthDelta > 0 && actualDepthDelta / 2 > max(3.0*max(-expectedDDA, -expectedDDB), 0.25))
							farInFrontOfNeighbour = 1;
						if (signedDepthDelta < 0 && actualDepthDelta / 2 < min(min(-expectedDDA, -expectedDDB), -0.5))
							farBehindNeighbour = 1;
					}
				#endif

				edge = (((comparison > 0.5 && weAreFavored) || (!nbrPixelated) || (farInFrontOfNeighbour)) && !farBehindNeighbour);

				// peel normals backwards if neighbour is neither us, pixelated, or a depth-identified normal edge.
				float3 nbrNormal = lerp(nbr.normalVS, float3(0, 0, -0.3), (1 - nbrPixelated * sameID * !farInFrontOfNeighbour));
				// If behind neighbour, don't include neighbour in average - we are probably occluded.
				nbrNormal = lerp(nbrNormal, us.normalVS, farBehindNeighbour);
				average = lerp(average, us.normalVS + nbrNormal, edge);

				// For Debugging:
				// return 1 - farInFrontOfNeighbour;
				return 1 - edge;
			}
#endif

			void frag(ProPVaryings i, out float4 color: COLOR) {
			
				float2 mainTexel = i.scrPos.xy;
				float2 pShift = float2(_TexelSize.x, _TexelSize.y);
				float4 packed = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex_point_clamp, mainTexel);
				Neighbour us = buildNeighbour(mainTexel, float2(0, 0), packed);
				#if DEPTH_TEST_NORMAL_EDGES_ON
					us.positionWS = getWorldSpacePosition(us.texel, us.depth);
				#endif
				
				if (us.pixelSize < 1)
				{
					// if this pixel is not pixelised, just return main texture colour.
					// Note that we must return a sensible normal in the rg components, to prevent NaN at edges.
					color = float4(0,0,0,0);
					return;
				}
				
				Neighbour neighbours[9];
				neighbours[0] = sampleNeighbour(mainTexel, float2(-1, 1), us.pixelSize);
				neighbours[1] = sampleNeighbour(mainTexel, float2( 0, 1), us.pixelSize);
				neighbours[2] = sampleNeighbour(mainTexel, float2( 1, 1), us.pixelSize);
				neighbours[3] = sampleNeighbour(mainTexel, float2(-1, 0), us.pixelSize);
				neighbours[4] = us;
				neighbours[5] = sampleNeighbour(mainTexel, float2( 1, 0), us.pixelSize);
				neighbours[6] = sampleNeighbour(mainTexel, float2(-1,-1), us.pixelSize);
				neighbours[7] = sampleNeighbour(mainTexel, float2( 0,-1), us.pixelSize);
				neighbours[8] = sampleNeighbour(mainTexel, float2( 1,-1), us.pixelSize);

				float countSimilar = 0;
				[unroll]
				for (int i = 0; i < 9; i++) {
					countSimilar += compareIDs(us, neighbours[i]);
				}

				float IDfactor = countSimilar > 7 ? 0.0 : 1.0;

				// Edge detection through normals.
				#if NORMAL_EDGE_DETECTION_ON
					float edgeFactorInverse = 1;
					float3 average = us.normalVS;
					edgeFactorInverse *= edgeTest(us, neighbours[1], average); // up
					edgeFactorInverse *= edgeTest(us, neighbours[5], average); // right
					edgeFactorInverse *= edgeTest(us, neighbours[7], average); // down
					edgeFactorInverse *= edgeTest(us, neighbours[3], average); // left
					average = normalize(average);
					float edgeFactor = 1 - edgeFactorInverse;

					// Debug: check output from edgeTest for specific direction.
					//edgeFactor = edgeTest(us, neighbours[5], average); // right
				#else
					float edgeFactor = 0;
					float3 average = float3(0, 0, 1);
				#endif
				color = PackOutline(IDfactor, edgeFactor, average);

				// Debug: test world-space positions work.
				// color = float4(frac(us.positionWS / 10.0), 1.0);
			}
		
		ENDHLSL
		}
	}
	//FallBack "Diffuse"
}
