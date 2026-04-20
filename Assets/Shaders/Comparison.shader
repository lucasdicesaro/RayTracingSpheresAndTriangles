Shader "Custom/Comparison"
{
    Properties {}

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Inputs
            sampler2D _MainTex;     // new render (source of Blit)
            sampler2D _MainTexOld;  // previous accumulated render
            int NumRenderedFrames;  // frame counter

            float4 frag(v2f i) : SV_Target
            {
                float4 oldRender = tex2D(_MainTexOld, i.uv);
                float4 newRender = tex2D(_MainTex, i.uv);

                // Weighted average over frames
                float weight = 1.0 / (NumRenderedFrames + 1);
                float4 accumulativeAverage = oldRender * (1 - weight) + newRender * weight;
                return accumulativeAverage;
            }
            ENDCG
        }
    }
}
