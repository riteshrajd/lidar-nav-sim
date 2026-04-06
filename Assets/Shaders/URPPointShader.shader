Shader "Custom/URPPointShader"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 5.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // PSIZE is required for rendering MeshTopology.Points without flickering
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float size : PSIZE;
            };

            float _PointSize;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                // Outputting PSIZE stops URP from exploding generic points into random flickering polygons!
                output.size = _PointSize;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Simple Unlit color
                return input.color;
            }
            ENDHLSL
        }
    }
}
