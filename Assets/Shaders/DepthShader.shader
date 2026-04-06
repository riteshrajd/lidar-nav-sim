// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Custom/DepthShader"
{
	Properties{
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" }

		Pass
		{
			CGPROGRAM

			#pragma target 4.0
			#pragma vertex vert
			#pragma fragment frag
			#pragma glsl
			#include "UnityCG.cginc"

			struct v2f
			{
				float4 pos : SV_POSITION;
				float4 color: COLOR;
				float4 posProj : TEXCOORD0;
				float2 tex : TEXCOORD1;
				float3 objNormal : TEXCOORD2;
				float3 normal: NORMAL;
			};

			sampler2D _MainTex;
			fixed4 _Color;
			//float _DetailNormalMapScale;
			float _SegmentationId;

			v2f vert(appdata_full v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.posProj = mul(UNITY_MATRIX_MV, v.vertex);
				o.normal = mul(UNITY_MATRIX_MV, float4(v.normal, 0)).xyz;
				o.objNormal = mul(UNITY_MATRIX_M, float4(v.normal, 0)).xyz;
				o.tex = v.texcoord.xy;
				o.color = v.color;
				return o;
			}

			float4 frag(v2f input) : COLOR
			{
				float range = min(1,length(input.posProj.xyz) / _ProjectionParams.z);
				float NdotL = dot(normalize(input.normal), normalize(-input.posProj.xyz));
				float4 color = tex2D(_MainTex, input.tex.xy) * _Color;
				float luminosity = 0.21 * color.r + 0.72 * color.g + 0.007 * color.b;
				float lum = 0.9 * luminosity + 0.1 * NdotL;

				// first three decimal => intensity, last two decimal => reflectivity
				//lum = round(lum * 1000) / 1000 +_Reflectivity * .00001f;
				//lum = _Reflectivity * .01;

				//if (_Color.a < 1)
					//range = 1;
				float3 normalRange = normalize(input.objNormal) * range;
				//return float4(range, lum, _DetailNormalMapScale, _DetailNormalMapScale + lum);
				//return float4(normalRange.xyz, _DetailNormalMapScale + lum);

				return float4(normalRange.xyz, _SegmentationId + lum);
				// return float4(1, 0, 0, 1);
			}

			ENDCG
		}
	}
}