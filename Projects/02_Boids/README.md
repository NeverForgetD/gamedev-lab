# 🐟 Boids 기반 군집 행동 시뮬레이션과 최적화

이번 스터디는 Boids 알고리즘을 기반으로 다수의 개체가 자연스럽게 군집 행동을 하도록 구현하는 것을 목표로 합니다.

Boids는 각각의 개체가 복잡한 AI를 갖는 것이 아니라, 주변 개체를 기준으로 단순한 규칙을 따르도록 하여 자연스러운 군집 행동을 만들어내는 알고리즘입니다.

기본 Boids 구현부터 시작해, 게임 월드 적용, 공간 분할 최적화, 최종 데모 제작까지 진행합니다.

<!-- 📺 **전체 플레이리스트** → [Boids Dev Log — Unity](링크) -->

---

## 🛠️ Tech Stack

- **Engine:** Unity 6 (URP)
- **Language:** C#
- **Mesh:** ProBuilder

---

## 🧠 Boids 핵심 개념

### 1. Separation (분리)

![Separation](Images/Seperation.png)

- 너무 가까운 이웃과는 멀어지는 규칙
- 개체끼리 서로 겹치거나 뭉치는 것을 방지
- 예시: 가까운 Boid가 있다면 반대 방향으로 이동한다

### 2. Alignment (조정)

![Alignment](Images/Alignment.png)

- 주변 이웃들의 평균 이동 방향에 맞추는 규칙
- 군집이 전체적으로 비슷한 방향으로 흐르도록 만든다
- 예시: 주변 Boid들이 오른쪽으로 이동하고 있다면, 나도 오른쪽 방향으로 조금 맞춘다

### 3. Cohesion (응집)

![Cohesion](Images/Cohesion.png)

- 주변 이웃들의 평균 위치, 즉 군집 중심 쪽으로 이동하는 규칙
- 개체들이 너무 흩어지지 않고 하나의 무리를 유지하도록 만든다
- 예시: 주변 Boid들의 중심점을 향해 이동한다

---

## ⚙️ 구현 과정

### 1. 🐦 Boids 기본 알고리즘 구현

> CPU에서 Separation, Alignment, Cohesion 세 가지 규칙을 이용해 기본적인 군집 움직임을 구현합니다.

<!-- > ▶ [Week 1](링크) -->

![Boids_1주차](Images/Boids_1주차.gif)

#### BoidData & BoidSettings

각 Boid의 상태는 `BoidData` struct로 관리합니다. 매 프레임 모든 Boid의 배열을 순회할 때 불필요한 컴포넌트 참조 없이 위치와 방향만 전달할 수 있도록 가볍게 유지합니다.

```csharp
public struct BoidData
{
    public Vector3 position;
    public Vector3 direction;
}
```

군집 행동에 필요한 파라미터는 `BoidSettings`에 모아 `BoidsManager`에서 중앙 관리합니다.

#### Separation / Alignment / Cohesion

세 규칙 모두 `perceptionRadius`(또는 `separationRadius`) 안의 이웃 Boid를 순회해 평균값을 구한 뒤, `SteerTowards`로 조향력을 계산합니다.

**🔴 Separation** — 너무 가까운 이웃을 밀어냄. 거리에 반비례해 가중치를 높여 가까울수록 강하게 반응합니다.

```csharp
foreach (var boid in allBoids)
{
    float dist = Vector3.Distance(transform.position, boid.position);
    if (dist > 0f && dist < s.separationRadius)
    {
        avgAvoid += (transform.position - boid.position).normalized / dist;
        count++;
    }
}
```

**🟡 Alignment** — 이웃들의 평균 방향으로 맞춤.

```csharp
foreach (var boid in allBoids)
{
    float dist = Vector3.Distance(transform.position, boid.position);
    if (dist > 0f && dist < s.perceptionRadius)
    {
        avgDir += boid.direction;
        count++;
    }
}
```

**🟢 Cohesion** — 이웃들의 평균 위치(중심점)를 향해 이동.

```csharp
foreach (var boid in allBoids)
{
    float dist = Vector3.Distance(transform.position, boid.position);
    if (dist > 0f && dist < s.perceptionRadius)
    {
        avgPosition += boid.position;
        count++;
    }
}
return SteerTowards(avgPosition - transform.position, s);
```

세 힘은 각각의 weight를 곱해 합산되고, `SteerTowards`를 통해 `maxSteerForce`로 클램핑된 조향 벡터로 변환됩니다.

```csharp
Vector3 acceleration = Separation(allBoids, s) * s.separationWeight
                     + Alignment(allBoids, s)  * s.alignmentWeight
                     + Cohesion(allBoids, s)   * s.cohesionWeight;
```

```csharp
// Reynolds steering: 목표 방향의 최대 속도 벡터에서 현재 속도를 뺀 값
Vector3 steer = desired.normalized * maxSpeed - velocity;
return Vector3.ClampMagnitude(steer, maxSteerForce);
```

> **⚠️ 현재 시간복잡도: O(N²)**  
> 매 프레임 모든 Boid가 나머지 모든 Boid를 순회하기 때문에, 개체 수가 늘어날수록 연산량이 제곱으로 증가합니다.  
> **3주차에서 Spatial Hash 또는 Uniform Grid를 도입해 주변 탐색 범위를 제한하고 O(N) 수준으로 개선할 예정입니다.**

---

### 2. 🧱 장애물 회피 (Obstacle Avoidance)

> 단순히 떠다니는 군집이 아니라, 게임 월드 안에서 장애물을 스스로 피하며 움직이는 군집으로 확장합니다.

<!-- > ▶ [Week 2](링크) -->

![Boids_2주차_00](Images/Boids_2주차_00.gif)

#### BoidHelper — 황금비 나선 구면 샘플링

장애물 회피의 핵심은 "막히지 않은 방향"을 빠르게 찾는 것입니다. 이를 위해 `BoidHelper`는 구면 위에 300개의 방향 벡터를 미리 계산해 static으로 보관합니다.

방향 벡터를 생성할 때 **인덱스 순서**가 핵심입니다. `t = i / N`으로 inclination이 0 → π로 선형 증가하기 때문에, 인덱스 0번은 정면(forward), 뒤로 갈수록 옆·뒤쪽 방향으로 퍼집니다. 여기에 **황금비(φ ≈ 1.618)** 를 azimuth 증분으로 사용합니다. 황금비는 어떤 분수로도 정확히 근사되지 않는 특성 때문에 나선이 절대 겹치거나 한쪽에 몰리지 않고, 각 인덱스 구간에서 구면 위의 방향이 고르게 분포됩니다.

이 두 특성이 결합된 결과, `ObstacleRays`에서 인덱스 0번부터 순회하면 **균등하게 퍼진 방향들을 정면에서부터 차례로 탐색**하게 됩니다. 별도의 정렬이나 거리 비교 없이 단순 순회만으로, 첫 번째로 발견한 통과 방향이 자연스럽게 현재 진행 방향과 가장 가까운 열린 방향이 됩니다.

```csharp
float goldenRatio = (1 + Mathf.Sqrt(5)) / 2;
float angleIncrement = Mathf.PI * 2 * goldenRatio;

for (int i = 0; i < numViewDirections; i++)
{
    float t = (float)i / numViewDirections;
    float inclination = Mathf.Acos(1 - 2 * t);
    float azimuth = angleIncrement * i;

    float x = Mathf.Sin(inclination) * Mathf.Cos(azimuth);
    float y = Mathf.Sin(inclination) * Mathf.Sin(azimuth);
    float z = Mathf.Cos(inclination);
    directions[i] = new Vector3(x, y, z);
}
```

인덱스 순서대로 forward(정면)에서 시작해 점점 옆과 뒤쪽으로 퍼져나가는 구조입니다. 이 순서가 이후 장애물 회피에서 핵심 역할을 합니다.

#### 장애물 감지 — IsHeadingForCollision

매 프레임 현재 진행 방향(forward)으로 `SphereCast`를 쏩니다. `collisionAvoidanceDistance` 안에 장애물이 감지되면 회피 로직을 시작합니다.

```csharp
Physics.SphereCast(transform.position, s.collisionRadius, transform.forward,
    out hit, s.collisionAvoidanceDistance, s.collisionMask)
```

ray가 아닌 sphere를 쓰는 이유는 Boid 자체의 부피를 고려해야 하기 때문입니다. `collisionRadius`는 Boid의 실제 크기에 맞게 설정합니다.

#### 대체 방향 탐색 — ObstacleRays

장애물이 감지되면 `BoidHelper.directions`의 300개 방향을 인덱스 0번부터 순서대로 순회합니다. 각 방향으로 동일한 `SphereCast`를 쏘고, **막히지 않는 첫 번째 방향을 즉시 반환**합니다.

```csharp
for (int i = 0; i < dirs.Length; i++)
{
    Vector3 dir = transform.TransformDirection(dirs[i]);
    Ray ray = new Ray(transform.position, dir);
    if (!Physics.SphereCast(ray, s.collisionRadius, s.collisionAvoidanceDistance, s.collisionMask))
        return dir;
}
```

황금비 나선 순서 덕분에 정면에 가까운 방향부터 탐색하게 되고, 결과적으로 **현재 진행 방향을 최소한으로 바꾸는 경로**를 자연스럽게 선택합니다. 찾은 방향은 `collisionAvoidanceWeight(10f)`가 곱해진 강한 조향력으로 적용되어 다른 flocking 규칙을 압도합니다.

#### 디버그 기즈모 — DrawDebugGizmos

구현 과정을 시각적으로 확인하기 위해 `GizmoType` enum으로 표시 모드를 제어하는 디버그 기즈모를 추가했습니다.

```csharp
public enum GizmoType { Never, SelectedOnly, Always }
```

`SelectedOnly`로 설정하면 Hierarchy에서 선택한 Boid 하나에만 기즈모가 표시됩니다.

| 색상 | 의미 |
|------|------|
| 회색 점 (300개) | `BoidHelper.directions` 구면 위 방향 샘플 |
| 초록 선 | forward SphereCast 통과 — 장애물 없음 |
| 빨간 선 | forward SphereCast 감지 — 회피 로직 활성화 |
| 반투명 짧은 빨간 선 | `ObstacleRays`에서 막힌 방향 |
| 하얀 선 | `ObstacleRays`에서 찾은 첫 번째 통과 방향 (실제 조향 목표) |


![Boids_2주차_01](Images/Boids_2주차_01.gif)
![Boids_2주차_02](Images/Boids_2주차_02.gif)

---

### 3. ⚡ 공간 분할 최적화 & 렌더링 최적화 _(임시)_

> 모든 Boid가 모든 Boid를 검사하는 O(N²) 방식의 한계를 확인하고, Spatial Hash 또는 Uniform Grid를 이용해 주변 탐색을 최적화합니다.  
> 그 외 GPU Instancing, 오브젝트 풀링, 디버깅 시각화, 파라미터 프리셋 등을 적용해 다수의 Boid가 안정적으로 동작하도록 폴리싱합니다.  
> 전체 Boid 위치/방향을 `ComputeBuffer`에 담아 GPU Compute Shader로 처리합니다.

<!-- > ▶ [Week 3](링크) -->

---

### 4. 🎬 최종 데모 제작과 폴리싱 _(임시)_

> 앞서 구현한 Boids 알고리즘, 게임플레이 확장, 최적화 과정을 정리하고 최종 데모를 다듬습니다.  
> 성능 비교, 파라미터 변화, 구현 중 겪은 문제와 해결 방법을 발표 자료 또는 기술 블로그 형태로 공유합니다.

<!-- > ▶ [Week 4](링크) -->

---

## 📅 개발 일지

| 주차 | 핵심 내용 | 영상 |
|------|-----------|------|
| Week 1 | Boids 기본 알고리즘 · Separation · Alignment · Cohesion | <!-- [▶ Week 1](링크) --> |
| Week 2 | 장애물 회피 · 환경 상호작용 · 행동 확장 | <!-- [▶ Week 2](링크) --> |
| Week 3 | 공간 분할 최적화 · GPU Instancing · 렌더링 최적화 | <!-- [▶ Week 3](링크) --> |
| Week 4 | 최종 데모 · 폴리싱 · 발표 | <!-- [▶ Week 4](링크) --> |
