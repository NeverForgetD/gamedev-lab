using UnityEngine;

public class BoidSpawner : MonoBehaviour
{
    public GameObject boidPrefab;
    public int boidCount = 100;
    public float spawnRadius = 10f;
    public BoidSettings settings;

    public BoidController[] Spawn()
    {
        var boids = new BoidController[boidCount];
        for (int i = 0; i < boidCount; i++)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * spawnRadius;
            GameObject go = Instantiate(boidPrefab, pos, Random.rotation);
            boids[i] = go.GetComponent<BoidController>();
            boids[i].Init(Random.onUnitSphere * settings.minSpeed, settings);
        }
        return boids;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}
