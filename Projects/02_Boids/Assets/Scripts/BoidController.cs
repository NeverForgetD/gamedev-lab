using UnityEngine;

public class BoidController : MonoBehaviour
{
    [HideInInspector] public Vector3 velocity;
    public BoidData data;

    public enum GizmoType { Never, SelectedOnly, Always }

    [Header("Debug")]
    public GizmoType debugGizmos = GizmoType.Never;
    private BoidSettings cachedSettings;

    public void Init(Vector3 initialVelocity)
    {
        velocity = initialVelocity;
        data = new BoidData(transform.position, velocity.normalized);
    }

    public void UpdateBoid(BoidData[] allBoids, BoidSettings s, BoidContext ctx)
    {
        cachedSettings = s;
        Vector3 acceleration = Separation(allBoids, s) * s.separationWeight
                             + Alignment(allBoids, s)  * s.alignmentWeight
                             + Cohesion(allBoids, s)   * s.cohesionWeight
                             + TargetSeek(ctx, s)      * s.targetWeight
                             + PredatorFlee(ctx, s)    * s.predatorWeight;

        if (IsHeadingForCollision(s))
        {
            Vector3 clearDir = ObstacleRays(s);
            acceleration += SteerTowards(clearDir, s) * s.collisionAvoidanceWeight;
        }

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

    private Vector3 TargetSeek(BoidContext ctx, BoidSettings s)
    {
        if (!ctx.hasTarget) return Vector3.zero;
        return SteerTowards(ctx.targetPosition - transform.position, s);
    }

    private Vector3 PredatorFlee(BoidContext ctx, BoidSettings s)
    {
        if (!ctx.hasPredator) return Vector3.zero;
        Vector3 diff = transform.position - ctx.predatorPosition;
        float dist = diff.magnitude;
        if (dist > s.predatorRadius || dist < 0.001f) return Vector3.zero;
        return SteerTowards(diff.normalized / dist, s);
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

    bool IsHeadingForCollision(BoidSettings s)
    {
        RaycastHit hit;
        if (Physics.SphereCast(transform.position, s.collisionRadius, transform.forward, out hit, s.collisionAvoidanceDistance, s.collisionMask))
            return true;
        return false;
    }

    private Vector3 ObstacleRays(BoidSettings s)
    {
        Vector3[] dirs = BoidHelper.directions;
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 dir = transform.TransformDirection(dirs[i]);
            Ray ray = new Ray(transform.position, dir);
            if (!Physics.SphereCast(ray, s.collisionRadius, s.collisionAvoidanceDistance, s.collisionMask))
                return dir;
        }
        return transform.forward;
    }

    private void OnDrawGizmos()
    {
        if (debugGizmos == GizmoType.Always) DrawDebugGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (debugGizmos == GizmoType.SelectedOnly) DrawDebugGizmos();
    }

    private void DrawDebugGizmos()
    {
        if (cachedSettings == null) return;

        float dist = cachedSettings.collisionAvoidanceDistance;
        float r    = cachedSettings.collisionRadius;
        int   mask = cachedSettings.collisionMask;

        // BoidHelper.directions — 구면 위 300개 점
        //Gizmos.color = Color.white;
        Gizmos.color = new Color(0.4f, 0.4f, 0.4f);
        foreach (var dir in BoidHelper.directions)
        {
            Vector3 worldDir = transform.TransformDirection(dir);
            Gizmos.DrawSphere(transform.position + worldDir * dist, 0.04f);
        }

        // Forward SphereCast (IsHeadingForCollision) 상태 표시
        bool headingForCollision = Physics.SphereCast(
            transform.position, r, transform.forward, out _, dist, mask);

        Gizmos.color = headingForCollision ? Color.red : Color.green;
        Gizmos.DrawRay(transform.position, transform.forward * dist);

        // ObstacleRays — blocked: 짧은 빨간 선 / first clear: 시안 선
        if (headingForCollision)
        {
            Vector3[] dirs = BoidHelper.directions;
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 worldDir = transform.TransformDirection(dirs[i]);
                Ray ray = new Ray(transform.position, worldDir);
                bool blocked = Physics.SphereCast(ray, r, dist, mask);

                if (!blocked)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawRay(transform.position, worldDir * dist);
                    break;
                }

                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
                Gizmos.DrawRay(transform.position, worldDir * dist * 0.4f);
            }
        }
    }
}
