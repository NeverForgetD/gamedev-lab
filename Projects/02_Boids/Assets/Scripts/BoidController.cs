using UnityEngine;

public class BoidController : MonoBehaviour
{
    [Header("Speed")]
    public float minSpeed = 2f;
    public float maxSpeed = 5f;

    [Header("Perception")]
    public float perceptionRadius = 3f;
    public float separationRadius = 1.5f;

    [Header("Weights")]
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;

    [HideInInspector] public Vector3 velocity;

    private BoidData[] _allBoids;

    public void Init(Vector3 initialVelocity)
    {
        velocity = initialVelocity;
    }

    public void UpdateBoid(BoidData[] allBoids)
    {
        _allBoids = allBoids;

        Vector3 separation = Separation();
        Vector3 alignment = Alignment();
        Vector3 cohesion = Cohesion();

        Vector3 acceleration = separation * separationWeight
                             + alignment  * alignmentWeight
                             + cohesion   * cohesionWeight;

        velocity += acceleration * Time.deltaTime;
        velocity = ClampSpeed(velocity);

        transform.position += velocity * Time.deltaTime;

        if (velocity.sqrMagnitude > 0.001f)
            transform.forward = velocity.normalized;
    }

    private Vector3 Separation()
    {
        Vector3 steer = Vector3.zero;
        int count = 0;

        foreach (var boid in _allBoids)
        {
            float dist = Vector3.Distance(transform.position, boid.position);
            if (dist > 0f && dist < separationRadius)
            {
                Vector3 away = (transform.position - boid.position).normalized / dist;
                steer += away;
                count++;
            }
        }

        if (count > 0)
            steer /= count;

        return steer;
    }

    private Vector3 Alignment()
    {
        Vector3 avgVelocity = Vector3.zero;
        int count = 0;

        foreach (var boid in _allBoids)
        {
            float dist = Vector3.Distance(transform.position, boid.position);
            if (dist > 0f && dist < perceptionRadius)
            {
                avgVelocity += boid.velocity;
                count++;
            }
        }

        if (count == 0)
            return Vector3.zero;

        avgVelocity /= count;
        return (avgVelocity - velocity).normalized;
    }

    private Vector3 Cohesion()
    {
        Vector3 avgPosition = Vector3.zero;
        int count = 0;

        foreach (var boid in _allBoids)
        {
            float dist = Vector3.Distance(transform.position, boid.position);
            if (dist > 0f && dist < perceptionRadius)
            {
                avgPosition += boid.position;
                count++;
            }
        }

        if (count == 0)
            return Vector3.zero;

        avgPosition /= count;
        return (avgPosition - transform.position).normalized;
    }

    private Vector3 ClampSpeed(Vector3 v)
    {
        float speed = v.magnitude;
        if (speed < minSpeed)
            return v.normalized * minSpeed;
        if (speed > maxSpeed)
            return v.normalized * maxSpeed;
        return v;
    }
}
