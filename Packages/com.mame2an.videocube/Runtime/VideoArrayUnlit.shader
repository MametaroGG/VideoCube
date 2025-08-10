Shader "Mame2an/VideoArrayUnlit"
{
    Properties {
        _TexArr ("Texture Array", 2DArray) = "white" {}
        _Frame  ("Frame", Float) = 0
        _Gamma  ("Gamma", Float) = 2.2
        _Emissive("Emissive", Float) = 1.0
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Back
        ZWrite Off
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEX2DARRAY(_TexArr);
            float _Frame; float _Gamma; float _Emissive;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v){ v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(v2f i):SV_Target {
                int idx = (int)round(_Frame);
                fixed4 col = UNITY_SAMPLE_TEX2DARRAY(_TexArr, float3(i.uv, idx));
                col.rgb = pow(saturate(col.rgb), 1.0/_Gamma) * _Emissive;
                return col;
            }
            ENDHLSL
        }
    }
}