
Shader "Custom/SPH2D_Fast"
{
	Properties
	{
		_MainTex("Texture",         2D) = "black" {}
	_ParticleRadius("Particle Radius", Float) = 0.05
	}

		CGINCLUDE
#include "UnityCG.cginc"

		struct v2g
	{
		float3 pos   : POSITION_SV;
		float4 color : COLOR;
	};

	struct g2f
	{
		float4 pos   : POSITION;
		float2 tex   : TEXCOORD0;
		float4 color : COLOR;
	};

	struct FluidParticle
	{
		float3 position;
		float3 velocity;
	};

	struct FluidParticleDensity
	{
		float density;
	};

	sampler2D _MainTex;
	float4 _MainTex_ST;

	StructuredBuffer<FluidParticle> _ParticlesBuffer;
	StructuredBuffer<FluidParticleDensity> _ParticlesDensityBuffer;

	float  _ParticleRadius;
	float4x4 _InvViewMatrix;

	fixed3 rgb2hsv(fixed3 c) {
		fixed4 K = fixed4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
		fixed4 p = lerp(fixed4(c.b, c.g, K.w, K.z), fixed4(c.g, c.b, K.x, K.y), step(c.b, c.g));
		fixed4 q = lerp(fixed4(p.x, p.y, p.w, c.r), fixed4(c.r, p.y, p.z, p.x), step(p.x, c.r));

		float d = q.x - min(q.w, q.y);
		float e = 1.0e-10;
		return fixed3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
	}

	fixed3 hsv2rgb(fixed3 c) {
		fixed4 K = fixed4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
		fixed3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
		return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
	}

	// --------------------------------------------------------------------
	// Vertex Shader
	// --------------------------------------------------------------------
	v2g vert(uint id : SV_VertexID)
	{
		v2g o = (v2g)0;
		o.pos = float3(_ParticlesBuffer[id].position.xyz);
		
		// 位置
		//o.color = float4(normalize(_ParticlesBuffer[id].position.xyz), 1.0);

		// 速度
		o.color = float4(0.5 + 0.5 * normalize(_ParticlesBuffer[id].velocity.xyz), 1.0);

		// 密度
		//o.color = float4(0.5,0.5,_ParticlesDensityBuffer[id].density*0.001, 1.0);

		float density = _ParticlesDensityBuffer[id].density;
		//o.color = float4(hsv2rgb(float3(-0.45, saturate(smoothstep(0.2, 0.9, density * 0.0005)), 0.5)), 1.0);
		return o;
	}

	static const float3 g_positions[4] = {
		float3(-1, 1, 0),
		float3(1, 1, 0),
		float3(-1,-1, 0),
		float3(1,-1, 0),
	};

	static const float2 g_texcoords[4] = {
		float2(0, 0),
		float2(1, 0),
		float2(0, 1),
		float2(1, 1),
	};

	// --------------------------------------------------------------------
	// Geometry Shader
	// --------------------------------------------------------------------
	[maxvertexcount(4)]
	void geom(point v2g In[1], inout TriangleStream<g2f> SpriteStream)
	{
		g2f output = (g2f)0;
		[unroll]
		for (int i = 0; i < 4; i++)
		{
			float3 position = g_positions[i] * _ParticleRadius;
			position = mul(_InvViewMatrix, position) + In[0].pos;
			output.pos = mul(UNITY_MATRIX_MVP, float4(position, 1.0));

			output.color = In[0].color;
			output.tex = g_texcoords[i];
			SpriteStream.Append(output);
		}
		SpriteStream.RestartStrip();
	}

	// --------------------------------------------------------------------
	// Fragment Shader
	// --------------------------------------------------------------------
	fixed4 frag(g2f input) : SV_Target
	{
		return tex2D(_MainTex, input.tex) * input.color;
	}
		ENDCG

		SubShader
	{
		Tags{ "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
			LOD 100

			ZWrite Off
			Blend One One

			Pass
		{
			CGPROGRAM
#pragma target   5.0
#pragma vertex   vert
#pragma geometry geom
#pragma fragment frag
			ENDCG
		}
	}
}