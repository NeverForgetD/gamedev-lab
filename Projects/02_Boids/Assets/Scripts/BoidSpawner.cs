using UnityEngine;

public class BoidSpawner : MonoBehaviour
{
    public GameObject boidPrefab;
    public int boidCount = 100;
    public float spawnRadius = 20f;
    public LayerMask obstacleLayer;

    const int maxAttempts = 30;

    public BoidController[] Spawn()
    {
        var boids = new BoidController[boidCount];
        for (int i = 0; i < boidCount; i++)
        {
            Vector3 pos = GetValidPosition();
            GameObject go = Instantiate(boidPrefab, pos, Random.rotation);
            boids[i] = go.GetComponent<BoidController>();
            boids[i].Init(Random.onUnitSphere);
        }
        return boids;
    }

    private Vector3 GetValidPosition()
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * spawnRadius;
            if (!Physics.CheckSphere(pos, 0.5f, obstacleLayer))
                return pos;
        }
        return transform.position + Random.insideUnitSphere * spawnRadius;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}
