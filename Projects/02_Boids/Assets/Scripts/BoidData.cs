using UnityEngine;

public struct BoidData
{
    public Vector3 position;
    public Vector3 direction;

    public BoidData(Vector3 position, Vector3 direction)
    {
        this.position = position;
        this.direction = direction;
    }
}
