using UnityEngine;

public class BoidController : MonoBehaviour
{
    [HideInInspector] public Vector3 velocity;

    private BoidSettings _settings;
    private BoidData[] _allBoids;

    public void Init(Vector3 initialVelocity, BoidSettings settings)
    {
        velocity = initialVelocity;
        _settings = settings;
    }

    public void UpdateBoid(BoidData[] allBoids)
    {
        _allBoids = allBoids;

        Vector3 separation = Separation();
        Vector3 alignment = Alignment();
        Vector3 cohesion = Cohesion();

        Vector3 acceleration = separation * _settings.separationWeight
                             + alignment  * _settings.alignmentWeight
                             + cohesion   * _settings.cohesionWeight;

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
            if (dist > 0f && dist < _settings.separationRadius)
            {
                steer += (transform.position - boid.position).normalized / dist;
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
            if (dist > 0f && dist < _settings.perceptionRadius)
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
            if (dist > 0f && dist < _settings.perceptionRadius)
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
        if (speed < _settings.minSpeed) return v.normalized * _settings.minSpeed;
        if (speed > _settings.maxSpeed) return v.normalized * _settings.maxSpeed;
        return v;
    }
}
