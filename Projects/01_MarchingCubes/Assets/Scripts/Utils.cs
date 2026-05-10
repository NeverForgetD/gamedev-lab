using Unity.Mathematics;

public static class Utils
{
    public static int Flatten(int3 p, int resolution)
        => p.x + p.y * resolution + p.z * resolution * resolution;
}
