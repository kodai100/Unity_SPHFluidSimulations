Shader "Custom/dot" {
	SubShader{
		ZWrite On
		Blend SrcAlpha OneMinusSrcAlpha

		Pass{
			CGPROGRAM

			#pragma target 5.0
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"

			struct Particle {
				float3 pos;
				float3 vel;
				float3 force;
				float density;
				float pressure;
			};

			StructuredBuffer<Particle> _Particles;

			struct v2f {
				float4 col : COLOR;
			};

			v2f vert(uint id : SV_VertexID) {
				v2f output;
				output.col = float4(0.5 + normalize(_Particles[id].vel) / 2, 1);
				return output;
			}

			fixed4 frag(v2f i) : COLOR{
				return i.col;
			}

			ENDCG
		}
	}
}