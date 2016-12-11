using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace SPH3D_FAST {
    public class SPH3D : MonoBehaviour {

        const int SIMULATION_BLOCK_SIZE = 32;
        public ComputeShader particleCS;

        ComputeBuffer ParticleBufferRead;
        ComputeBuffer ParticleBufferWrite;
        ComputeBuffer GridBufferRead;
        ComputeBuffer GridBufferWrite;

        // ---------- For SPH ------------
        public int numParticles;
        public float restDensity = 600.0f;
        public float pressureStiff = 3.0f; // 堅さ(?)
        public float p_mass = 0.00020543f; // 粒子質量
        public float simulationScale = 0.004f; // シミュレートするスケール
        public float smoothStep = 0.01f; // 有効半径(CELL_WIDTH)
        public float dt = 0.004f; // ΔT
        public float viscosity = 0.2f; // 粘性
        public float vel_limit = 200.0f; // 速度制限
        public float radius = 0.004f; // 半径
        public float wallStiff = 10000.0f;   // 壁の反発力
        public float restitution = 256.0f;
        public Vector3 min = new Vector3(0.0f, 0.0f, 0.0f);
        public Vector3 max = new Vector3(50.0f, 50.0f, 50.0f);
        public Vector3 gravity = new Vector3(0, -10, 0);
        private float densityCoef;
        private float gradPressureCoef;
        private float lapViscosityCoef;

        public Vector3 GridDim = new Vector3(20.0f, 50.0f, 10.0f);
        // ---------- For SPH ------------

        

        void Start() {
            InitializeVariables();
            InitializeComputeBuffer();
        }

        public ComputeBuffer GetParticleBuffer() {
            return this.ParticleBufferRead;
        }

        public int GetMaxParticleNum() {
            return this.numParticles;
        }

        void Update() {
            int threadGroupSize = Mathf.CeilToInt(numParticles / SIMULATION_BLOCK_SIZE) + 1;

            particleCS.SetInt("_NumParticles", numParticles);
            particleCS.SetFloat("_RestDensity", restDensity);
            particleCS.SetFloat("_PressureStiff", pressureStiff);
            particleCS.SetFloat("_PMass", p_mass);
            particleCS.SetFloat("_SimScale", simulationScale);
            particleCS.SetFloat("_SmoothStep", smoothStep);
            particleCS.SetFloat("_DT", dt);
            particleCS.SetFloat("_Viscosity", viscosity);
            particleCS.SetFloat("_VelLimit", vel_limit);
            particleCS.SetFloat("_Radius", radius);
            particleCS.SetFloat("_WallStiff", wallStiff);
            particleCS.SetFloat("_Restitution", restitution);
            particleCS.SetVector("_Min", min);
            particleCS.SetVector("_Max", max);
            particleCS.SetVector("_Gravity", gravity);
            particleCS.SetFloat("_DensityCoef", densityCoef);
            particleCS.SetFloat("_GradPressureCoef", gradPressureCoef);
            particleCS.SetFloat("_LapViscosityCoef", lapViscosityCoef);
            particleCS.SetVector("_GridDim", GridDim);

            // Build Grid ----------------------------------------------------------------
            int kernel = particleCS.FindKernel("buildGrid");
            particleCS.SetBuffer(kernel, "_ParticleBufferRead", ParticleBufferRead);
            particleCS.SetBuffer(kernel, "_GridBufferRead", GridBufferRead);
            particleCS.SetBuffer(kernel, "_GridBufferWrite", GridBufferWrite);
            particleCS.Dispatch(kernel, threadGroupSize, 1, 1);
            SwapBuffer(ref GridBufferRead, ref GridBufferWrite);

            //HashGridKeyValue[] h = new HashGridKeyValue[numParticles];
            //GridBufferRead.GetData(h);
            //Debug.Log(h[10].key >> 16); // h[particle_id]:cell z
            // Build Grid ----------------------------------------------------------------

            kernel = particleCS.FindKernel("sph");
            particleCS.SetBuffer(kernel, "_ParticleBufferRead", ParticleBufferRead);
            particleCS.SetBuffer(kernel, "_ParticleBufferWrite", ParticleBufferWrite);
            //particleCS.SetBuffer(kernel, "_GridSorted", GridSorted);

            particleCS.Dispatch(kernel, threadGroupSize, 1, 1);

            SwapBuffer(ref ParticleBufferRead, ref ParticleBufferWrite);
        }

        void InitializeVariables() {
            densityCoef = 315.0f / (64.0f * Mathf.PI * Mathf.Pow(smoothStep, 9.0f));
            gradPressureCoef = -45.0f / (Mathf.PI * Mathf.Pow(smoothStep, 6.0f));
            lapViscosityCoef = 45.0f / (Mathf.PI * Mathf.Pow(smoothStep, 6.0f));
        }

        void InitializeComputeBuffer() {
            List<Particle> particles = new List<Particle>();
            for (float x = 0; x <= numParticles; x++) {
                particles.Add(new Particle((max + min)/2 + Random.insideUnitSphere * 10));
            }

            ParticleBufferRead = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(Particle)));
            ParticleBufferWrite = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(Particle)));
            ParticleBufferRead.SetData(particles.ToArray());

            GridBufferRead = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(HashGridKeyValue)));
            GridBufferWrite = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(HashGridKeyValue)));
        }

        void SwapBuffer(ref ComputeBuffer src, ref ComputeBuffer dst) {
            ComputeBuffer tmp = src;
            src = dst;
            dst = tmp;
        }

        void ReleaseBuffer() {
            if (ParticleBufferRead != null) {
                ParticleBufferRead.Release();
                ParticleBufferRead = null;
            }
            if (ParticleBufferWrite != null) {
                ParticleBufferWrite.Release();
                ParticleBufferWrite = null;
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
            Gizmos.DrawWireCube((max + min) / 2, max - min);
        }

        void OnDestroy() {
            ReleaseBuffer();
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