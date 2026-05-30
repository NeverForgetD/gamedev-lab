using UnityEngine;

public class BoidsManager : MonoBehaviour
{
    [Header("Spawn")]
    public GameObject boidPrefab;
    public int boidCount = 100;
    public float spawnRadius = 10f;

    [Header("Boundary")]
    public float boundaryRadius = 20f;
    public float boundaryForce = 5f;

    private BoidController[] _boids;
    private BoidData[] _boidData;

    private void Start()
    {
        _boids = new BoidController[boidCount];
        _boidData = new BoidData[boidCount];

        for (int i = 0; i < boidCount; i++)
        {
            Vector3 spawnPos = transform.position + Random.insideUnitSphere * spawnRadius;
            GameObject go = Instantiate(boidPrefab, spawnPos, Random.rotation);
            _boids[i] = go.GetComponent<BoidController>();

            Vector3 initialVel = Random.onUnitSphere * _boids[i].minSpeed;
            _boids[i].Init(initialVel);
        }
    }

    private void Update()
    {
        // 이번 프레임의 스냅샷 수집
        for (int i = 0; i < boidCount; i++)
            _boidData[i] = new BoidData(_boids[i].transform.position, _boids[i].velocity);

        // 각 Boid 업데이트
        for (int i = 0; i < boidCount; i++)
        {
            ApplyBoundary(_boids[i]);
            _boids[i].UpdateBoid(_boidData);
        }
    }

    private void ApplyBoundary(BoidController boid)
    {
        Vector3 offset = boid.transform.position - transform.position;
        if (offset.magnitude > boundaryRadius)
        {
            // 경계를 벗어나면 중심 방향으로 속도 보정
            boid.velocity += -offset.normalized * boundaryForce * Time.deltaTime;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, boundaryRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}
