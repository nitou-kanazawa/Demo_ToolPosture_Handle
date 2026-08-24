Shader "ToolPosture/GizmoVertexColor"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            // LightMode タグを付けない: URP は既定で SRPDefaultUnlit として扱い、
            // Built-in RP でも描画対象になる (タグを付けると Built-in で描かれない)

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Tint;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.color * _Tint;
            }
            ENDCG
        }
    }

    Fallback Off
}
