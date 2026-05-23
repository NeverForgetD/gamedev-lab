// DensityFieldJob.cs
// SimpleDensityField의 main-thread 루프를 IJobParallelFor + Burst 로 이전.
//
// ▸ CreateSphereJob    : 구형 밀도장 초기화
// ▸ CreateTerrain2DJob : 2D 노이즈 지형 밀도장 초기화
// ▸ ModifyDensityJob   : 반경 내 deltaField 누적 (terrain edit)
// ▸ ApplyDeltaJob      : deltaField 를 densityField 에 반영

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// ─────────────────────────────────────────────────────────────────────────────
// 구형 밀도장
// ─────────────────────────────────────────────────────────────────────────────
[BurstCompile]
public struct CreateSphereJob : IJobParallelFor
{
    // ── 입력 ──────────────────────────────────────────────────────────
    [ReadOnly] public NativeArray<float> DeltaField;

    public int    PointsPerAxis; // resolution + 1
    public float3 CenterPos;    // transform.position (월드 원점)
    public float3 FieldCenter;  // 밀도 계산용 로컬 중심
    public float  UnitSize;
    public float  SphereRadius;

    // 노이즈
    public bool  ApplyNoise;
    public float NoiseFrequency;
    public float NoiseAmplitude;

    // ── 출력 ──────────────────────────────────────────────────────────
    public NativeArray<FieldData> DensityField;

    // ── 실행 ──────────────────────────────────────────────────────────
    public void Execute(int i)
    {
        int pts = PointsPerAxis;
        var x   = i % pts;
        var y   = (i / pts) % pts;
        var z   = i / (pts * pts);
        var pos = (new float3(x, y, z) - FieldCenter) * UnitSize + CenterPos;

        float density = math.distance(pos, CenterPos) - SphereRadius;
        if (ApplyNoise)
            density += FBm3D(pos, NoiseFrequency) * NoiseAmplitude;

        DensityField[i] = new FieldData
        {
            position = pos,
            density  = density + DeltaField[i]
        };
    }

    // ── fBm 3D ────────────────────────────────────────────────────────
    private static float FBm3D(float3 p, float frequency)
    {
        const int   OCTAVES    = 4;
        const float LACUNARITY = 2f;
        const float GAIN       = 0.5f;

        float value = 0f, amp = 1f, freq = frequency, norm = 0f;
        for (int i = 0; i < OCTAVES; i++)
        {
            value += noise.snoise(p * freq) * amp;
            norm  += amp;
            freq  *= LACUNARITY;
            amp   *= GAIN;
        }
        return value / norm;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2D 노이즈 지형 밀도장
// ─────────────────────────────────────────────────────────────────────────────
[BurstCompile]
public struct CreateTerrain2DJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> DeltaField;

    public int    PointsPerAxis;
    public float3 OriginPos;
    public float3 FieldCenter;
    public float  UnitSize;
    public float  BaseHeight;

    public bool  ApplyNoise;
    public float NoiseFrequency;
    public float NoiseAmplitude;

    public NativeArray<FieldData> DensityField;

    public void Execute(int i)
    {
        int pts = PointsPerAxis;
        var x   = i % pts;
        var y   = (i / pts) % pts;
        var z   = i / (pts * pts);
        var pos = (new float3(x, y, z) - FieldCenter) * UnitSize + OriginPos;

        float density = pos.y - BaseHeight;
        if (ApplyNoise)
            density -= FBm2D(new float2(pos.x, pos.z), NoiseFrequency) * NoiseAmplitude;

        DensityField[i] = new FieldData
        {
            position = pos,
            density  = density + DeltaField[i]
        };
    }

    // ── fBm 2D ────────────────────────────────────────────────────────
    private static float FBm2D(float2 p, float frequency)
    {
        const int   OCTAVES    = 4;
        const float LACUNARITY = 2f;
        const float GAIN       = 0.5f;

        float value = 0f, amp = 1f, freq = frequency, norm = 0f;
        for (int i = 0; i < OCTAVES; i++)
        {
            value += noise.snoise(p * freq) * amp;
            norm  += amp;
            freq  *= LACUNARITY;
            amp   *= GAIN;
        }
        return value / norm;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 지형 편집 : 반경 내 deltaField 누적
// ─────────────────────────────────────────────────────────────────────────────
[BurstCompile]
public struct ModifyDensityJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<FieldData> DensityField; // 위치 참조용

    public NativeArray<float> DeltaField;

    public float3 Center;
    public float  Radius;
    public float  Delta;

    public void Execute(int i)
    {
        float distSq   = math.distancesq(DensityField[i].position, Center);
        float radiusSq = Radius * Radius;
        if (distSq >= radiusSq) return;

        float t = 1f - math.sqrt(distSq) / Radius;
        DeltaField[i] += Delta * t * t;
    }
}
