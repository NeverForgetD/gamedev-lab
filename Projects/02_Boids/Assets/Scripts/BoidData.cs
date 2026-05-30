using UnityEngine;

public struct BoidData
{
    public Vector3 position;
    public Vector3 velocity;

    public BoidData(Vector3 position, Vector3 velocity)
    {
        this.position = position;
        this.velocity = velocity;
    }
}
