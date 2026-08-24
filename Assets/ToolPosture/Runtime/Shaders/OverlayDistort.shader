Shader "ToolPosture/OverlayDistort"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _K1 ("Radial k1", Float) = -0.2
        _Aspect ("Aspect (w/h)", Float) = 1.3333
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            // LightMode タグを付けない (URP / Built-in の両方で描画対象になる)

            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float _K1;
            float _Aspect;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // i.uv は歪んだ画像側の座標。歪みの無いレンダー結果のどこを引くかを求める。
                // DistortedOverlayViewport.Undistort と同じ式。
                float2 n = (i.uv - 0.5) * 2.0;
                n.x *= _Aspect;

                // 折り返しの向こう側は逆写像が一意でないので手前へ丸める
                // (DistortedOverlayViewport.MaxUndistortableRadius と同じ)
                if (_K1 < 0.0)
                {
                    float maxRadius = sqrt(-1.0 / (3.0 * _K1)) * (2.0 / 3.0);
                    float radius = length(n);
                    if (radius > maxRadius && radius > 1e-6) n *= maxRadius / radius;
                }

                // r(1 + k1 r^2) = rd をニュートン法で解く (C# 側と同じ)
                float rd = length(n);
                float r = rd;
                if (rd > 1e-6)
                {
                    for (int k = 0; k < 6; k++)
                    {
                        float rr = r * r;
                        float dg = 1.0 + 3.0 * _K1 * rr;
                        if (abs(dg) < 1e-5) break;
                        r -= (r * (1.0 + _K1 * rr) - rd) / dg;
                    }
                }
                float2 nu = (rd > 1e-6) ? n * (r / rd) : n;

                nu.x /= _Aspect;
                float2 uv = nu * 0.5 + 0.5;

                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return fixed4(0.02, 0.02, 0.03, 1.0);

                return tex2D(_MainTex, uv) * _Color * i.color;
            }
            ENDCG
        }
    }

    Fallback Off
}
