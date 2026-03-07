Shader "Fluid/Fluid2DComposite"
{
    Properties
    {
        _MainTex ("Background", 2D) = "white" {}
		_WaterColor ("Water Color", Color) = (0.4, 0.7, 1, 1)
		_ThicknessScale ("Thickness To Alpha", Float) = 4.0
		_ThicknessDarken ("Thickness Darken", Float) = 1.5
    }
    SubShader
    {
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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;      // standard quad UV (use for fluid data)
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Keep fluid data in native RT space so it lines up with world positions
                o.uv = v.uv;
                return o;
            }

			sampler2D _MainTex;
			sampler2D Comp;
			sampler2D Normals;
			float4 _WaterColor;
			float _ThicknessScale;
			float _ThicknessDarken;

            float4 frag (v2f i) : SV_Target
            {
                // Background: flip V so the copied camera image is right-side up (command-buffer Blit convention)
                float2 uvBg = float2(i.uv.x, 1.0 - i.uv.y);
                float4 bg = tex2D(_MainTex, uvBg);

                // Fluid data: use native UV so depth/thickness/normals align with world (and with corrected background)
                float4 packedData = tex2D(Comp, i.uv);
				float depth = packedData.r;
				float thickness = packedData.g;

				// Normals from reconstruction pass (world space, float RT so already in [-1,1])
				float3 n = tex2D(Normals, i.uv).xyz;
				float len = length(n);
				n = len > 0.001 ? normalize(n) : float3(0, 0, 1);

				// Directional lighting (top-right for 2D; matches common “sky” direction)
				const float3 lightDir = normalize(float3(0.35, 0.85, 0.4));
				float nDotL = saturate(dot(n, lightDir));
				float lighting = lerp(0.35, 1.4, nDotL);

				// Base water colour with lighting
				float3 waterCol = _WaterColor.rgb * lighting;

				// Darken where fluid is thick (depth; effect is strong so you can see it)
				float inside = saturate(thickness * _ThicknessDarken);
				waterCol = lerp(waterCol, waterCol * 0.2, inside);

				// Convert thickness to alpha; tweak scale for desired opacity.
				float alpha = saturate(thickness * _ThicknessScale);

				// Simple alpha blend over background.
				float3 col = lerp(bg.rgb, waterCol, alpha);
				return float4(col, 1);
            }
            ENDCG
        }
    }
}

