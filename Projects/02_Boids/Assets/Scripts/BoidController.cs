using UnityEngine;

public class BoidController : MonoBehaviour
{
    [HideInInspector] public Vector3 velocity;

    private BoidData[] _allBoids;

    public void Init(Vector3 initialVelocity)
    {
        velocity = initialVelocity;
    }

    public void UpdateBoid(BoidData[] allBoids, BoidSettings s)
    {
        _allBoids = allBoids;

        Vector3 acceleration = Separation(s) * s.separationWeight
                             + Alignment(s)  * s.alignmentWeight
                             + Cohesion(s)   * s.cohesionWeight;

        velocity += acceleration * Time.deltaTime;
        velocity = ClampSpeed(velocity, s);

        transform.position += velocity * Time.deltaTime;

        if (velocity.sqrMagnitude > 0.001f)
            transform.forward = velocity.normalized;
    }

    private Vector3 Separation(BoidSettings s)
    {
        Vector3 steer = Vector3.zero;
        int count = 0;

        foreach (var boid in _allBoids)
        {
            float dist = Vector3.Distance(transform.position, boid.position);
            if (dist > 0f && dist < s.separationRadius)
            {
                steer += (transform.position - boid.position).normalized / dist;
                count++;
            }
        }

        if (count > 0) steer /= count;
        return steer;
    }

    private Vector3 Alignment(BoidSettings s)
    {
        Vector3 avgVelocity = Vector3.zero;
        int count = 0;

        foreach (var boid in _allBoids)
        {
            float dist = Vector3.Distance(transform.position, boid.position);
            if (dist > 0f && dist < s.perceptionRadius)
            {
                avgVelocity += boid.velocity;
                count++;
            }
        }

        if (count == 0) return Vector3.zero;
        avgVelocity /= count;
        return (avgVelocity - velocity).normalized;
    }

    private Vector3 Cohesion(BoidSettings s)
    {
        Vector3 avgPosition = Vector3.zero;
        int count = 0;

        foreach (var boid in _allBoids)
        {
            float dist = Vector3.Distance(transform.position, boid.position);
            if (dist > 0f && dist < s.perceptionRadius)
            {
                avgPosition += boid.position;
                count++;
            }
        }

        if (count == 0) return Vector3.zero;
        avgPosition /= count;
        return (avgPosition - transform.position).normalized;
    }

    private Vector3 ClampSpeed(Vector3 v, BoidSettings s)
    {
        float speed = v.magnitude;
        if (speed < s.minSpeed) return v.normalized * s.minSpeed;
        if (speed > s.maxSpeed) return v.normalized * s.maxSpeed;
        return v;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, transform.forward);
    }
}
