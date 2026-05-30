using UnityEngine;

public class BoidsManager : MonoBehaviour
{
    [Header("References")]
    public BoidSpawner spawner;

    [Header("Settings")]
    public BoidSettings settings;

    [Header("Boundary")]
    public float boundaryRadius = 20f;
    public float boundaryForce = 5f;

    private BoidController[] _boids;
    private BoidData[] _boidData;

    private void Start()
    {
        _boids = spawner.Spawn();
        _boidData = new BoidData[_boids.Length];
    }

    private void Update()
    {
        for (int i = 0; i < _boids.Length; i++)
            _boidData[i] = new BoidData(_boids[i].transform.position, _boids[i].velocity);

        for (int i = 0; i < _boids.Length; i++)
        {
            ApplyBoundary(_boids[i]);
            _boids[i].UpdateBoid(_boidData, settings);
        }
    }

    private void ApplyBoundary(BoidController boid)
    {
        Vector3 offset = boid.transform.position - transform.position;
        if (offset.magnitude > boundaryRadius)
            boid.velocity += -offset.normalized * boundaryForce * Time.deltaTime;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, boundaryRadius);
    }
}
