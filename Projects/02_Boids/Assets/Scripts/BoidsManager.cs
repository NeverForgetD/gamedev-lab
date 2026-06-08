using UnityEngine;

public class BoidsManager : MonoBehaviour
{
    [Header("References")]
    public BoidSpawner spawner;

    [Header("Settings")]
    public BoidSettings settings;

    [Header("Target")]
    public Transform target;

    [Header("Predator")]
    public Transform predator;

    [Header("Boundary")]
    public float boundaryRadius = 20f;

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
            _boidData[i] = _boids[i].data;

        BoidContext ctx = new BoidContext
        {
            hasTarget       = target != null,
            targetPosition  = target   != null ? target.position   : Vector3.zero,
            hasPredator     = predator != null,
            predatorPosition = predator != null ? predator.position : Vector3.zero
        };

        for (int i = 0; i < _boids.Length; i++)
        {
            //ApplyBoundary(_boids[i]);
            _boids[i].UpdateBoid(_boidData, settings, ctx);
        }
    }

    private void ApplyBoundary(BoidController boid)
    {
        Vector3 offset = boid.transform.position - transform.position;
        if (offset.magnitude > boundaryRadius)
        {
            Vector3 outward = offset.normalized;
            float outwardSpeed = Vector3.Dot(boid.velocity, outward);
            if (outwardSpeed > 0f)
                boid.velocity -= 2f * outwardSpeed * outward;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, boundaryRadius);
    }
}
