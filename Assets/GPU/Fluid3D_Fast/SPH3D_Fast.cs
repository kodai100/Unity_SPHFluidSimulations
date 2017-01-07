using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

struct FluidParticle {
    public Vector3 Position;
    public Vector3 Velocity;
};

struct FluidParticleDensity {
    public float Density;
};

struct FluidParticleForces {
    public Vector3 Acceleration;
};

struct Uint2 {
    public uint x;
    public uint y;
};


public class SPH3D_Fast : MonoBehaviour {

    #region ComputeShaderConstants
    // Grid cell key size for sorting 8-bits for x and y;
    const int NUM_GRID_INDICES = 65536;

    // Numthreads size for the simulation
    const int SIMULATION_BLOCK_SIZE = 256;

    // Numthreads size for the sort
    const int BITONIC_BLOCK_SIZE = 512;
    const int TRANSPOSE_BLOCK_SIZE = 16;

    const int NUM_PARTICLES_8K = 8 * 1024;
    const int NUM_PARTICLES_16K = 16 * 1024;
    const int NUM_PARTICLES_32K = 32 * 1024;
    const int NUM_PARTICLES_64K = 64 * 1024;

    #endregion

    public enum NumParticlesSet {
        NUM_8K,
        NUM_16K,
        NUM_32K,
        NUM_64K
    };

    public NumParticlesSet ParticleNum = NumParticlesSet.NUM_16K;

    public float Smoothlen = 0.012f;
    public float PressureStiffness = 200.0f;
    public float RestDensity = 1000.0f;
    public float ParticleMass = 0.0002f;
    public float Viscosity = 0.1f;
    public float MaxAllowableTimeStep = 0.005f;
    public float WallStiffness = 3000.0f;
    public Vector3 _containerCenter = new Vector3(0, 0, 0);
    public Vector3 _containerSize = new Vector3(50, 50, 50);

    public Vector3 Gravity = new Vector3(0.0f, -0.5f, 0.0f);

    int _numParticles;
    float _timeStep;
    float _smoothlen;
    float _pressureStiffness;
    float _restDensity;
    float _densityCoef;
    float _gradPressureCoef;
    float _lapViscosityCoef;
    float _wallStiffness;
    float _particleMass;
    Vector4 _gravity;
    
    Vector4 _gridDim;

    #region ComputeBuffer
    ComputeBuffer _particlesBuffer;
    ComputeBuffer _sortedParticlesBuffer;
    ComputeBuffer _particleDensityBuffer;
    ComputeBuffer _particleForcesBuffer;

    ComputeBuffer _gridBuffer;
    ComputeBuffer _gridPingPongBuffer;

    ComputeBuffer _gridIndicesBuffer;

    #endregion

    public ComputeShader FluidCS;
    public ComputeShader BitnicSortCS;

    bool _isInitParams = false;

    #region Accessor
    public ComputeBuffer GetParticlesBuffer() {
        return this._particlesBuffer;
    }

    public ComputeBuffer GetParticlesDensityBuffer() {
        return this._particleDensityBuffer;
    }

    public int GetNumParticles() {
        return this._numParticles;
    }
    #endregion

    #region MonoBehaviour Functions
    void Start() {
        Init();
    }

    void Update() {
        Step();
    }

    void OnDestroy() {
        DeleteBuffer(_particlesBuffer);
        DeleteBuffer(_sortedParticlesBuffer);
        DeleteBuffer(_particleDensityBuffer);
        DeleteBuffer(_particleForcesBuffer);
        DeleteBuffer(_gridBuffer);
        DeleteBuffer(_gridPingPongBuffer);
        DeleteBuffer(_gridIndicesBuffer);
    }

    void OnDrawGizmos() {
        Gizmos.DrawWireCube(_containerCenter, _containerSize);
    }

    void OnGUI() {

        int ox = 20;
        int oy = 20;
        int dy = 15;

        GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));

        GUI.Label(new Rect(ox, oy + dy * 0, 512, 20), "_numParticles : " + _numParticles);
        GUI.Label(new Rect(ox, oy + dy * 1, 512, 20), "_particleMass : " + _particleMass);
        GUI.Label(new Rect(ox, oy + dy * 2, 512, 20), "_timeStep : " + _timeStep);
        GUI.Label(new Rect(ox, oy + dy * 3, 512, 20), "_smoothlen : " + _smoothlen);
        GUI.Label(new Rect(ox, oy + dy * 4, 512, 20), "_pressureStiffness : " + _pressureStiffness);
        GUI.Label(new Rect(ox, oy + dy * 5, 512, 20), "_restDensity : " + _restDensity);
        GUI.Label(new Rect(ox, oy + dy * 6, 512, 20), "_densityCoef : " + _densityCoef);
        GUI.Label(new Rect(ox, oy + dy * 7, 512, 20), "_gradPressureCoef : " + _gradPressureCoef);
        GUI.Label(new Rect(ox, oy + dy * 8, 512, 20), "_lapViscosityCoef : " + _lapViscosityCoef);
        GUI.Label(new Rect(ox, oy + dy * 9, 512, 20), "_wallStiffness : " + _wallStiffness);
        GUI.Label(new Rect(ox, oy + dy * 11, 512, 20), "_grabity : " + _gravity);

        GUILayout.EndArea();

    }
    #endregion

    #region Private Funcitons
    void Init() {
        switch (ParticleNum) {
            case NumParticlesSet.NUM_8K:
                _numParticles = NUM_PARTICLES_8K;
                break;

            case NumParticlesSet.NUM_16K:
                _numParticles = NUM_PARTICLES_16K;
                break;

            case NumParticlesSet.NUM_32K:
                _numParticles = NUM_PARTICLES_32K;
                break;

            case NumParticlesSet.NUM_64K:
                _numParticles = NUM_PARTICLES_64K;
                break;

            default:
                _numParticles = 64;
                break;
        }

        Debug.Log("numParticles : " + _numParticles);

        _particlesBuffer = new ComputeBuffer(_numParticles, Marshal.SizeOf(typeof(FluidParticle)));
        int startingWidth = (int)Mathf.Sqrt((float)_numParticles);

        var particles = new FluidParticle[_numParticles];
        for (int i = 0; i < _numParticles; i++) {
            particles[i].Velocity = Vector3.zero;
            particles[i].Position = _containerCenter + Random.insideUnitSphere * 0.1f;
        }
        _particlesBuffer.SetData(particles);

        _sortedParticlesBuffer = new ComputeBuffer(_numParticles, Marshal.SizeOf(typeof(FluidParticle)));
        _particleForcesBuffer = new ComputeBuffer(_numParticles, Marshal.SizeOf(typeof(FluidParticleForces)));
        _particleDensityBuffer = new ComputeBuffer(_numParticles, Marshal.SizeOf(typeof(FluidParticleDensity)));

        _gridBuffer = new ComputeBuffer(_numParticles, Marshal.SizeOf(typeof(Uint2)));
        _gridPingPongBuffer = new ComputeBuffer(_numParticles, Marshal.SizeOf(typeof(Uint2)));

        _gridIndicesBuffer = new ComputeBuffer(NUM_GRID_INDICES, Marshal.SizeOf(typeof(Uint2)));

        particles = null;
    }

    public void Step() {

        _particleMass = ParticleMass;

        _timeStep = Mathf.Min(MaxAllowableTimeStep, Time.deltaTime);
        _smoothlen = Smoothlen;
        _pressureStiffness = PressureStiffness;
        _restDensity = RestDensity;
        _densityCoef = _particleMass * 315.0f / (64.0f * Mathf.PI * Mathf.Pow(_smoothlen, 9));
        _gradPressureCoef = _particleMass * -45.0f / (Mathf.PI * Mathf.Pow(_smoothlen, 6));
        _lapViscosityCoef = _particleMass * Viscosity * 45.0f / (Mathf.PI * Mathf.Pow(_smoothlen, 6));

        _gravity = new Vector4(Gravity.x, Gravity.y, Gravity.z, 0.0f);

        _wallStiffness = WallStiffness;

        _gridDim.x = 1.0f / _smoothlen;
        _gridDim.y = 1.0f / _smoothlen;
        _gridDim.z = 1.0f / _smoothlen;
        _gridDim.w = 0.0f;



        // --------------------------------------------------------------------
        ComputeShader kernelCS = FluidCS;
        int kernelID = -1;

        int threadGroupsX = _numParticles / SIMULATION_BLOCK_SIZE;

        kernelCS.SetInt("_NumParticles", _numParticles);
        kernelCS.SetFloat("_TimeStep", _timeStep);
        kernelCS.SetFloat("_Smoothlen", _smoothlen);
        kernelCS.SetFloat("_PressureStiffness", _pressureStiffness);
        kernelCS.SetFloat("_RestDensity", _restDensity);
        kernelCS.SetFloat("_DensityCoef", _densityCoef);
        kernelCS.SetFloat("_GradPressureCoef", _gradPressureCoef);
        kernelCS.SetFloat("_LapViscosityCoef", _lapViscosityCoef);
        kernelCS.SetFloat("_WallStiffness", _wallStiffness);
        kernelCS.SetVector("_ContainerCenter", _containerCenter);
        kernelCS.SetVector("_ContainerSize", _containerSize);
        kernelCS.SetVector("_Gravity", _gravity);
        kernelCS.SetVector("_GridDim", _gridDim);

        // Build Grid
        kernelID = kernelCS.FindKernel("BuildGridCS");
        kernelCS.SetBuffer(kernelID, "_ParticlesBufferRead", _particlesBuffer);
        kernelCS.SetBuffer(kernelID, "_GridBufferWrite", _gridBuffer);
        kernelCS.Dispatch(kernelID, threadGroupsX, 1, 1);

        // Sort Grid
        GPUSort(_gridBuffer, _gridPingPongBuffer);

        // Build Grid Indices
        kernelID = kernelCS.FindKernel("ClearGridIndicesCS");
        kernelCS.SetBuffer(kernelID, "_GridIndicesBufferWrite", _gridIndicesBuffer);
        kernelCS.Dispatch(kernelID, NUM_GRID_INDICES / SIMULATION_BLOCK_SIZE, 1, 1);

        kernelID = kernelCS.FindKernel("BuildGridIndicesCS");
        kernelCS.SetBuffer(kernelID, "_GridBufferRead", _gridBuffer);
        kernelCS.SetBuffer(kernelID, "_GridIndicesBufferWrite", _gridIndicesBuffer);
        kernelCS.Dispatch(kernelID, threadGroupsX, 1, 1);

        // Rearrange
        kernelID = kernelCS.FindKernel("RearrangeParticlesCS");
        kernelCS.SetBuffer(kernelID, "_GridBufferRead", _gridBuffer);
        kernelCS.SetBuffer(kernelID, "_ParticlesBufferRead", _particlesBuffer);
        kernelCS.SetBuffer(kernelID, "_ParticlesBufferWrite", _sortedParticlesBuffer);
        kernelCS.Dispatch(kernelID, threadGroupsX, 1, 1);

        // Density
        kernelID = kernelCS.FindKernel("DensityCS_Grid");
        kernelCS.SetBuffer(kernelID, "_ParticlesBufferRead", _sortedParticlesBuffer);
        kernelCS.SetBuffer(kernelID, "_GridIndicesBufferRead", _gridIndicesBuffer);
        kernelCS.SetBuffer(kernelID, "_ParticlesDensityBufferWrite", _particleDensityBuffer);
        kernelCS.Dispatch(kernelID, threadGroupsX, 1, 1);

        // Force
        kernelID = kernelCS.FindKernel("ForceCS_Grid");
        kernelCS.SetBuffer(kernelID, "_ParticlesBufferRead", _sortedParticlesBuffer);
        kernelCS.SetBuffer(kernelID, "_ParticlesDensityBufferRead", _particleDensityBuffer);
        kernelCS.SetBuffer(kernelID, "_GridIndicesBufferRead", _gridIndicesBuffer);
        kernelCS.SetBuffer(kernelID, "_ParticlesForceBufferWrite", _particleForcesBuffer);
        kernelCS.Dispatch(kernelID, threadGroupsX, 1, 1);

        // Integrate
        kernelID = kernelCS.FindKernel("IntegrateCS");
        kernelCS.SetBuffer(kernelID, "_ParticlesBufferRead", _sortedParticlesBuffer);
        kernelCS.SetBuffer(kernelID, "_ParticlesForceBufferRead", _particleForcesBuffer);
        kernelCS.SetBuffer(kernelID, "_ParticlesBufferWrite", _particlesBuffer);
        kernelCS.Dispatch(kernelID, threadGroupsX, 1, 1);

    }

    void GPUSort(ComputeBuffer inBuffer, ComputeBuffer tempBuffer) {
        ComputeShader sortCS = BitnicSortCS;

        int KERNEL_ID_BITONICSORT = sortCS.FindKernel("BitonicSort");
        int KERNEL_ID_TRANSPOSE = sortCS.FindKernel("MatrixTranspose");

        uint NUM_ELEMENTS = (uint)_numParticles;
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
            sortCS.SetBuffer(KERNEL_ID_TRANSPOSE, "Data", tempBuffer);
            sortCS.Dispatch(KERNEL_ID_TRANSPOSE, (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), 1);

            // Sort the transposed column data
            sortCS.SetBuffer(KERNEL_ID_BITONICSORT, "Data", tempBuffer);
            sortCS.Dispatch(KERNEL_ID_BITONICSORT, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);

            // Transpose the data from buffer 2 back into buffer 1
            SetGPUSortConstants(sortCS, BITONIC_BLOCK_SIZE, level, MATRIX_HEIGHT, MATRIX_WIDTH);
            sortCS.SetBuffer(KERNEL_ID_TRANSPOSE, "Input", tempBuffer);
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

    void SwapComputeBuffer(ref ComputeBuffer ping, ref ComputeBuffer pong) {
        ComputeBuffer temp = ping;
        ping = pong;
        pong = temp;
    }

    void DeleteBuffer(ComputeBuffer buffer) {
        if (buffer != null) {
            buffer.Release();
            buffer = null;
        }
    }
    #endregion
}
