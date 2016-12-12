using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace SPH3D_FAST {
    public class SPH3D : MonoBehaviour {

        const int SIMULATION_BLOCK_SIZE = 256;
        const int NUM_GRID_INDICES = 16777216;

        const int BITONIC_BLOCK_SIZE = 512;
        const int TRANSPOSE_BLOCK_SIZE = 16;

        public ComputeShader particleCS;
        public ComputeShader BitonicSortCS;

        ComputeBuffer ParticleBuffer;
        ComputeBuffer SortedParticleBuffer;
        ComputeBuffer GridBuffer;
        ComputeBuffer GridPingPongForSort;
        ComputeBuffer GridIndicesBuffer;

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

        public Vector3 GridDim = new Vector3(256.0f, 256.0f, 256.0f);
        // ---------- For SPH ------------

        void Start() {
            InitializeVariables();
            InitializeComputeBuffer();
        }

        public ComputeBuffer GetParticleBuffer() {
            return this.ParticleBuffer;
        }

        public int GetMaxParticleNum() {
            return this.numParticles;
        }

        void Update() {
            int threadGroupSize = numParticles / SIMULATION_BLOCK_SIZE;

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
            particleCS.SetBuffer(kernel, "_ParticleBufferRead", ParticleBuffer);
            particleCS.SetBuffer(kernel, "_GridBufferWrite", GridBuffer);
            particleCS.Dispatch(kernel, threadGroupSize, 1, 1);

            sortDebug();
            // Build Grid ----------------------------------------------------------------

            // Sort ----------------------------------------------------------------------
            GPUSort(ref GridBuffer, GridPingPongForSort);
            sortDebug();
            // Sort ----------------------------------------------------------------------



            // Build Grid Indices --------------------------------------------------------
            kernel = particleCS.FindKernel("buildGridIndices");
            particleCS.SetBuffer(kernel, "_GridBufferRead", GridBuffer);
            particleCS.SetBuffer(kernel, "_GridIndicesBufferWrite", GridIndicesBuffer);
            particleCS.Dispatch(kernel, threadGroupSize, 1, 1);
            // Build Grid Indices --------------------------------------------------------

            
            // Rearrange
            kernel = particleCS.FindKernel("rearrangeParticles");
            particleCS.SetBuffer(kernel, "_GridBufferRead", GridBuffer);
            particleCS.SetBuffer(kernel, "_ParticleBufferRead", ParticleBuffer);
            particleCS.SetBuffer(kernel, "_ParticleBufferWrite", SortedParticleBuffer);
            particleCS.Dispatch(kernel, threadGroupSize, 1, 1);

            /*
            kernel = particleCS.FindKernel("sph");
            particleCS.SetBuffer(kernel, "_ParticleBufferRead", SortedParticleBuffer);
            particleCS.SetBuffer(kernel, "_GridIndicesBufferRead", GridIndicesBuffer);
            particleCS.SetBuffer(kernel, "_ParticleBufferWrite", ParticleBuffer);
            particleCS.Dispatch(kernel, threadGroupSize, 1, 1);
            */
            
        }

        void InitializeVariables() {
            densityCoef = 315.0f / (64.0f * Mathf.PI * Mathf.Pow(smoothStep, 9.0f));
            gradPressureCoef = -45.0f / (Mathf.PI * Mathf.Pow(smoothStep, 6.0f));
            lapViscosityCoef = 45.0f / (Mathf.PI * Mathf.Pow(smoothStep, 6.0f));
        }

        void InitializeComputeBuffer() {
            List<Particle> particles = new List<Particle>();
            for (int i = 0; i < numParticles; i++) {
                particles.Add(new Particle((max + min)/2 + Random.insideUnitSphere * 10));
            }

            ParticleBuffer = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(Particle)));
            ParticleBuffer.SetData(particles.ToArray());

            GridBuffer = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(HashGridKeyValue)));
            GridPingPongForSort = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(HashGridKeyValue)));

            // 初期化まで
            GridIndicesBuffer = new ComputeBuffer(NUM_GRID_INDICES, Marshal.SizeOf(typeof(GridIndices)));
            GridIndices[] gridIndices = new GridIndices[NUM_GRID_INDICES];
            for (int i = 0; i < NUM_GRID_INDICES; i++) {
                gridIndices[i] = new GridIndices(0, 0);
            }
            GridIndicesBuffer.SetData(gridIndices);

            SortedParticleBuffer = new ComputeBuffer(numParticles, Marshal.SizeOf(typeof(Particle)));
        }

        void SwapBuffer(ref ComputeBuffer src, ref ComputeBuffer dst) {
            ComputeBuffer tmp = src;
            src = dst;
            dst = tmp;
        }

        void ReleaseBuffer() {
            if (ParticleBuffer != null) {
                ParticleBuffer.Release();
                ParticleBuffer = null;
            }
            if (GridBuffer != null) {
                GridBuffer.Release();
                GridBuffer = null;
            }
            if (GridPingPongForSort != null) {
                GridPingPongForSort.Release();
                GridPingPongForSort = null;
            }
            if (GridIndicesBuffer != null) {
                GridIndicesBuffer.Release();
                GridIndicesBuffer = null;
            }
            if (SortedParticleBuffer != null) {
                SortedParticleBuffer.Release();
                SortedParticleBuffer = null;
            }
        }
        
        void OnDrawGizmos() {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube((max + min) / 2, max - min);
        }

        void OnDestroy() {
            ReleaseBuffer();
        }

        void sortDebug() {
            HashGridKeyValue[] h = new HashGridKeyValue[numParticles];
            GridBuffer.GetData(h);
            string tmp = "";
            for (int i = 0; i < 100; i++) {
                tmp += "["+ h[i].key + "," + h[i].value + "],";
            }
            Debug.Log(tmp); // h[particle_id]:cell z
        }

        void GPUSort(ref ComputeBuffer inBuffer, ComputeBuffer tmpBuffer) {
            ComputeShader sortCS = BitonicSortCS;

            int KERNEL_ID_BITONICSORT = sortCS.FindKernel("BitonicSort");
            int KERNEL_ID_TRANSPOSE = sortCS.FindKernel("MatrixTranspose");

            uint NUM_ELEMENTS = (uint)numParticles;
            uint MATRIX_WIDTH = BITONIC_BLOCK_SIZE;
            uint MATRIX_HEIGHT = (uint)NUM_ELEMENTS / BITONIC_BLOCK_SIZE;

            for (uint level = 2; level <= BITONIC_BLOCK_SIZE; level <<= 1) {
                SetGPUSortConstants(sortCS, level, level, MATRIX_HEIGHT, MATRIX_WIDTH);

                // Sort the row data
                sortCS.SetBuffer(KERNEL_ID_BITONICSORT, "Data", inBuffer);
                sortCS.Dispatch(KERNEL_ID_BITONICSORT, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
            }

            // Then sort the rows and columns for the levels > than the block size
            // Transpose. Sort the Columns. Transpose. Sort the Rows.
            for (uint level = (BITONIC_BLOCK_SIZE << 1); level <= NUM_ELEMENTS; level <<= 1) {
                // Transpose the data from buffer 1 into buffer 2
                SetGPUSortConstants(sortCS, level / BITONIC_BLOCK_SIZE, (level & ~NUM_ELEMENTS) / BITONIC_BLOCK_SIZE, MATRIX_WIDTH, MATRIX_HEIGHT);
                sortCS.SetBuffer(KERNEL_ID_TRANSPOSE, "Input", inBuffer);
                sortCS.SetBuffer(KERNEL_ID_TRANSPOSE, "Data", tmpBuffer);
                sortCS.Dispatch(KERNEL_ID_TRANSPOSE, (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), 1);

                // Sort the transposed column data
                sortCS.SetBuffer(KERNEL_ID_BITONICSORT, "Data", tmpBuffer);
                sortCS.Dispatch(KERNEL_ID_BITONICSORT, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);

                // Transpose the data from buffer 2 back into buffer 1
                SetGPUSortConstants(sortCS, BITONIC_BLOCK_SIZE, level, MATRIX_HEIGHT, MATRIX_WIDTH);
                sortCS.SetBuffer(KERNEL_ID_TRANSPOSE, "Input", tmpBuffer);
                sortCS.SetBuffer(KERNEL_ID_TRANSPOSE, "Data", inBuffer);
                sortCS.Dispatch(KERNEL_ID_TRANSPOSE, (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), 1);

                // Sort the row data
                sortCS.SetBuffer(KERNEL_ID_BITONICSORT, "Data", inBuffer);
                sortCS.Dispatch(KERNEL_ID_BITONICSORT, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
            }
        }

        void SetGPUSortConstants(ComputeShader cs, uint level, uint levelMask, uint width, uint height) {
            cs.SetInt("_Level", (int)level);
            cs.SetInt("_LevelMask", (int)levelMask);
            cs.SetInt("_Width", (int)width);
            cs.SetInt("_Height", (int)height);
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
    
    struct GridIndices {
        public uint start;
        public uint end;

        public GridIndices(uint start, uint end) {
            this.start = start;
            this.end = end;
        }
    }
}