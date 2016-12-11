using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace SPH3D_FAST {
    public class SPH3D : MonoBehaviour {

        const int SIMULATION_BLOCK_SIZE = 32;
        public ComputeShader particleCS;

        ComputeBuffer particleBufferRead;
        ComputeBuffer particleBufferWrite;
        ComputeBuffer GridBufferRead;
        ComputeBuffer GridBufferWrite;

        public int numParticles;

        // ---------- For SPH ------------
        public float SPH_RESTDENSITY = 600.0f;
        public float SPH_INTSTIFF = 3.0f; // 堅さ(?)
        public float SPH_PMASS = 0.00020543f; // 粒子質量
        public float SPH_SIMSCALE = 0.004f; // シミュレートするスケール
        public float H = 0.01f; // 有効半径(CELL_WIDTH)
        public static float PI = 3.141592653589793f;
        public float DT = 0.004f; // ΔT
        public float SPH_VISC = 0.2f; // 粘性
        public float SPH_LIMIT = 200.0f; // 速度制限
        public float SPH_RADIUS = 0.004f; // 半径
        public float SPH_EPSILON = 0.00001f; // 許容誤差
        public float SPH_EXTSTIFF = 10000.0f;   // 壁の反発力
        public float SPH_EXTDAMP = 256.0f;
        public Vector3 MIN = new Vector3(0.0f, 0.0f, -10.0f);
        public Vector3 MAX = new Vector3(20.0f, 50.0f, 10.0f);
        public Vector3 GRAVITY = new Vector3(0, -10, 0);
        private float Poly6Kern; // Poly6Kernel関数の定数
        private float SpikyKern; // SpikyKernel関数の定数
        private float LapKern; // LaplacianKernel関数の定数(Viscosity)

        public Vector3 GridDim = new Vector3(20.0f, 50.0f, 10.0f);
        // ---------- For SPH ------------

        void OnDestroy() {
            ReleaseBuffer();
        }

        void Start() {
            InitializeVariables();
            InitializeComputeBuffer();
        }

        public ComputeBuffer GetParticleBuffer() {
            return this.particleBufferRead;
        }

        public int GetMaxParticleNum() {
            return this.numParticles;
        }

        void Update() {
            int threadGroupSize = Mathf.CeilToInt(numParticles / SIMULATION_BLOCK_SIZE) + 1;

            particleCS.SetInt("_NumParticles", numParticles);
            particleCS.SetFloat("SPH_RESTDENSITY", SPH_RESTDENSITY);
            particleCS.SetFloat("SPH_INTSTIFF", SPH_INTSTIFF);
            particleCS.SetFloat("SPH_PMASS", SPH_PMASS);
            particleCS.SetFloat("SPH_SIMSCALE", SPH_SIMSCALE);
            particleCS.SetFloat("H", H);
            particleCS.SetFloat("PI", PI);
            particleCS.SetFloat("DT", DT);
            particleCS.SetFloat("SPH_VISC", SPH_VISC);
            particleCS.SetFloat("SPH_LIMIT", SPH_LIMIT);
            particleCS.SetFloat("SPH_RADIUS", SPH_RADIUS);
            particleCS.SetFloat("SPH_EPSILON", SPH_EPSILON);
            particleCS.SetFloat("SPH_EXTSTIFF", SPH_EXTSTIFF);
            particleCS.SetFloat("SPH_EXTDAMP", SPH_EXTDAMP);
            particleCS.SetVector("MIN", MIN);
            particleCS.SetVector("MAX", MAX);
            particleCS.SetVector("GRAVITY", GRAVITY);
            particleCS.SetFloat("Poly6Kern", Poly6Kern);
            particleCS.SetFloat("SpikyKern", SpikyKern);
            particleCS.SetFloat("LapKern", LapKern);
            particleCS.SetVector("_GridDim", GridDim);

            int kernel = particleCS.FindKernel("buildGrid");
            particleCS.SetBuffer(kernel, "_ParticleBufferRead", particleBufferRead);
            particleCS.SetBuffer(kernel, "_GridBufferRead", GridBufferRead);
            particleCS.SetBuffer(kernel, "_GridBufferWrite", GridBufferWrite);
            particleCS.Dispatch(kernel, threadGroupSize, 1, 1);
            SwapBuffer(ref GridBufferRead, ref GridBufferWrite);

            HashGridKeyValue[] h = new HashGridKeyValue[numParticles];
            GridBufferRead.GetData(h);
            Debug.Log(h[10].key >> 16);

            kernel = particleCS.FindKernel("sph");
            particleCS.SetBuffer(kernel, "_ParticleBufferRead", particleBufferRead);
            particleCS.SetBuffer(kernel, "_ParticleBufferWrite", particleBufferWrite);
            //particleCS.SetBuffer(kernel, "_GridSorted", GridSorted);

            particleCS.Dispatch(kernel, threadGroupSize, 1, 1);

            SwapBuffer(ref particleBufferRead, ref particleBufferWrite);
        }

        void InitializeVariables() {
            Poly6Kern = 315.0f / (64.0f * PI * Mathf.Pow(H, 9.0f));
            SpikyKern = -45.0f / (PI * Mathf.Pow(H, 6.0f));
            LapKern = 45.0f / (PI * Mathf.Pow(H, 6.0f));
        }

        void InitializeComputeBuffer() {

            // 箱の大きさに従ってパーティクルを生成
            List<Particle> particles = new List<Particle>();
            for (float x = 0; x <= numParticles; x++) {
                particles.Add(new Particle((MAX + MIN)/2 + Random.insideUnitSphere * 10));
            }

            particleBufferRead = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(Particle)));    // パーティクル数を定義
            particleBufferWrite = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(Particle)));
            particleBufferRead.SetData(particles.ToArray());

            GridBufferRead = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(HashGridKeyValue)));
            GridBufferWrite = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(HashGridKeyValue)));
        }

        void SwapBuffer(ref ComputeBuffer src, ref ComputeBuffer dst) {
            ComputeBuffer tmp = src;
            src = dst;
            dst = tmp;
        }

        void ReleaseBuffer() {
            if (particleBufferRead != null) {
                particleBufferRead.Release();
                particleBufferRead = null;
            }
            if (particleBufferWrite != null) {
                particleBufferWrite.Release();
                particleBufferWrite = null;
            }
            if (GridBufferRead != null) {
                GridBufferRead.Release();
                GridBufferRead = null;
            }
            if (GridBufferWrite != null) {
                GridBufferWrite.Release();
                GridBufferWrite = null;
            }
        }
        
        void OnDrawGizmos() {
            Gizmos.color = Color.yellow;

            Gizmos.DrawWireCube((MAX + MIN) / 2, MAX - MIN);
        }

    }

    struct Particle {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 force;
        public float density;
        public float pressure;

        public Particle(Vector3 pos) {
            this.position = pos;
            this.velocity = new Vector3(0f, 0f, 0f);
            this.force = new Vector3(0f, 0f, 0f);
            this.density = 0f;
            this.pressure = 0f;
        }
    }

    struct HashGridKeyValue {
        public uint key;
        public uint value;
    };
}