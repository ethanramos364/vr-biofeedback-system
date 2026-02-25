Shader "VR/PhaseScrambleDisplay"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _Brightness ("Brightness", Range(0.0, 2.0)) = 1.0
        _Contrast ("Contrast", Range(0.0, 2.0)) = 1.0
        _Offset ("Offset", Range(-1.0, 1.0)) = 0.5
        [Toggle] _ClampOutput ("Clamp to [0,1]", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100
        Cull Off      // Render both front and back faces
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float _Brightness;
            float _Contrast;
            float _Offset;
            float _ClampOutput;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                float2 uv = i.uv;
                // Flip U when seen from the back face so texture isn't mirrored
                if (facing < 0)
                    uv.x = 1.0 - uv.x;

                // Sample RFloat stimulus texture (single channel)
                float val = tex2D(_BaseMap, uv).r;

                // Linear remap: centers around Offset, scaled by Brightness * Contrast
                float remapped = _Offset + _Brightness * _Contrast * (val - 0.5);

                // Optional clamp to visible [0,1] range
                if (_ClampOutput > 0.5)
                    remapped = clamp(remapped, 0.0, 1.0);

                return fixed4(remapped, remapped, remapped, 1.0);
            }
            ENDCG
        }
    }

    // Built-in fallback if shader fails
    FallBack "Unlit/Texture"
}
