using UnityEngine;

public class BoidController : MonoBehaviour
{
    [HideInInspector] public Vector3 velocity;
    public BoidData data;

    public void Init(Vector3 initialVelocity)
    {
        velocity = initialVelocity;
        data = new BoidData(transform.position, velocity.normalized);
    }

    public void UpdateBoid(BoidData[] allBoids, BoidSettings s)
    {
        Vector3 acceleration = Separation(allBoids, s) * s.separationWeight
                             + Alignment(allBoids, s)  * s.alignmentWeight
                             + Cohesion(allBoids, s)   * s.cohesionWeight;

        velocity += acceleration * Time.deltaTime;
        velocity = ClampSpeed(velocity, s);

        transform.position += velocity * Time.deltaTime;

        if (velocity.sqrMagnitude > 0.001f)
            transform.forward = velocity.normalized;

        data.position = transform.position;
        data.direction = velocity.normalized;
    }

    private Vector3 Separation(BoidData[] allBoids, BoidSettings s)
    {
        Vector3 avgAvoid = Vector3.zero;
        int count = 0;

        foreach (var boid in allBoids)
        {
            float dist = Vector3.Distance(transform.position, boid.position);
            if (dist > 0f && dist < s.separationRadius)
            {
                avgAvoid += (transform.position - boid.position).normalized / dist;
                count++;
            }
        }

        if (count == 0) return Vector3.zero;
        avgAvoid /= count;
        return SteerTowards(avgAvoid, s);
    }

    private Vector3 Alignment(BoidData[] allBoids, BoidSettings s)
    {
        Vector3 avgDir = Vector3.zero;
        int count = 0;

        foreach (var boid in allBoids)
        {
            float dist = Vector3.Distance(transform.position, boid.position);
            if (dist > 0f && dist < s.perceptionRadius)
            {
                avgDir += boid.direction;
                count++;
            }
        }

        if (count == 0) return Vector3.zero;
        avgDir /= count;
        return SteerTowards(avgDir, s);
    }

    private Vector3 Cohesion(BoidData[] allBoids, BoidSettings s)
    {
        Vector3 avgPosition = Vector3.zero;
        int count = 0;

        foreach (var boid in allBoids)
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
        return SteerTowards(avgPosition - transform.position, s);
    }

    private Vector3 SteerTowards(Vector3 desired, BoidSettings s)
    {
        Vector3 steer = desired.normalized * s.maxSpeed - velocity;
        return Vector3.ClampMagnitude(steer, s.maxSteerForce);
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
