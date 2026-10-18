// Shader for the 3D tiles and the play surface (built-in render pipeline).
//
// Vertex data (see TileMesh.cs):
//   color.rgb  flat colour used where the surface is plain (tile walls, bevel, felt)
//   color.a    0 = use color.rgb, 1 = use the face texture, in between = blend (the bevel fades into the face art)
//   uv0        position inside the face, 0..1; mapped into the atlas through the per-tile _FaceRect
//   uv1.x      how strongly the per-tile _Glow colour shows on this vertex (rim brighter than face)
//
// Lighting is a fixed light direction in world space, so tilting the board changes how the faces are lit.
// Per-tile values are instanced properties, so all tiles draw in a handful of instanced calls.
Shader "Mahjong/Tile"
{
    Properties
    {
        _MainTex ("Face atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FaceRect ("Face UV rect (x y w h)", Vector) = (0,0,1,1)
        _Glow ("Glow colour (rgb) and strength (a)", Color) = (0,0,0,0)
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FaceRect)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Glow)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                half3 nrm : TEXCOORD1;
                float3 wpos : TEXCOORD2;
                fixed4 tint : TEXCOORD3;
                fixed4 glow : TEXCOORD4;
            };

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float4 fr = UNITY_ACCESS_INSTANCED_PROP(Props, _FaceRect);
                o.uv = fr.xy + v.uv * fr.zw;
                o.color = v.color;
                o.nrm = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                fixed4 g = UNITY_ACCESS_INSTANCED_PROP(Props, _Glow);
                o.glow = fixed4(g.rgb, g.a * v.uv1.x);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 albedo = i.color.rgb;
                if (i.color.a > 0.001)
                {
                    fixed3 tex = tex2D(_MainTex, i.uv).rgb;
                    albedo = lerp(albedo, tex, i.color.a);
                }

                // light comes from the upper left, in front of the table
                const float3 L = float3(-0.32, 0.50, -0.80);
                float3 N = normalize(i.nrm);
                float ndl = saturate(dot(N, normalize(L)));
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 H = normalize(normalize(L) + V);
                float spec = pow(saturate(dot(N, H)), 48.0) * 0.16;
                float lit = 0.46 + 0.62 * ndl;

                fixed3 c = albedo * i.tint.rgb * lit + spec;
                c = lerp(c, i.glow.rgb, saturate(i.glow.a));
                return fixed4(c, i.tint.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
