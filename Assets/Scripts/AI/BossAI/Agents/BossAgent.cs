using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// PPO 베이스 에이전트 — 도망치는 플레이어를 추적해서 충돌 시 보상
// MainGame 오브젝트를 복사해 병렬 학습할 수 있도록 모든 위치/회전을 LocalSpace 기준으로 처리
//
// BehaviorParameters 설정:
//   Vector Observation Size : 6
//   Continuous Actions      : 2
//   Max Step                : 5000 (권장)
[System.Obsolete("BossInferenceAgent 사용")]
public class BossAgent : Agent
{
    [Header("이동")]
    [SerializeField] private float _moveSpeed     = 8.4f;
    [SerializeField] private float _rotationSpeed = 540f;

    [Header("관측 설정")]
    [SerializeField] private float _maxDistance = 15f;             // 거리 정규화 기준

    [Header("스폰 설정")]
    [SerializeField] private Transform       _bossSpawnPoint;       // 보스 스폰 기준 Transform
    [SerializeField] private GameObject      _playerObject;         // 플레이어 오브젝트
    [SerializeField] private List<Transform> _playerSpawnPoints;    // 플레이어 스폰 후보 리스트

    private Renderer _renderer;
    private int   _currentEpisode    = 0;
    private float _cumulativeReward  = 0f;

    // ══════════════════════════════════════════════════════════
    // 초기화
    // ══════════════════════════════════════════════════════════

    public override void Initialize()
    {
        _renderer = GetComponent<Renderer>();
    }

    public override void OnEpisodeBegin()
    {
        if (_renderer != null) _renderer.material.color = Color.red;
        SpawnObjects();
        Debug.Log($"Episode {_currentEpisode} ended with reward: {_cumulativeReward:F2}");

    }

    // ── 에피소드마다 위치/회전 초기화 (모두 LocalSpace 기준) ──
    private void SpawnObjects()
    {
        // 보스 초기화
        if (_bossSpawnPoint != null)
        {
            transform.SetLocalPositionAndRotation(_bossSpawnPoint.localPosition, _bossSpawnPoint.localRotation);
        }
        else
        {
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
        }

        // 플레이어 랜덤 스폰
        if (_playerObject != null && _playerSpawnPoints != null && _playerSpawnPoints.Count > 0)
        {
            int       idx   = Random.Range(0, _playerSpawnPoints.Count);
            Transform point = _playerSpawnPoints[idx];
            _playerObject.transform.SetLocalPositionAndRotation(point.localPosition, point.localRotation);
        }
    }

    // ══════════════════════════════════════════════════════════
    // 관측 (6개) — EnvironmentRoot 기준 로컬 좌표로 계산
    // ══════════════════════════════════════════════════════════
    // [0-1] 플레이어 방향 (X, Z, 정규화)
    // [2]   플레이어까지 거리 (정규화)
    // [3-4] 보스 전방 방향 (X, Z)
    // [5]   전방과 플레이어 방향의 정렬도 (-1 ~ 1)
    // ══════════════════════════════════════════════════════════
    public override void CollectObservations(VectorSensor sensor)
    {
        if (_playerObject == null)
        {
            sensor.AddObservation(new float[6]);
            return;
        }

        // 방향/거리는 두 점의 차이 → 복사본 위치 오프셋이 상쇄되므로 월드 좌표 그대로 사용
        Vector3 toPlayer = _playerObject.transform.position - transform.position;
        float   distance = toPlayer.magnitude;
        Vector3 dirNorm  = distance > 0.001f ? toPlayer.normalized : Vector3.forward;

        sensor.AddObservation(dirNorm.x);                                  // [0]
        sensor.AddObservation(dirNorm.z);                                  // [1]
        sensor.AddObservation(Mathf.Clamp01(distance / _maxDistance));     // [2]
        sensor.AddObservation(transform.forward.x);                        // [3]
        sensor.AddObservation(transform.forward.z);                        // [4]
        sensor.AddObservation(Vector3.Dot(transform.forward, dirNorm));    // [5]
    }

    // ══════════════════════════════════════════════════════════
    // 행동 (이산 1Branch, 4가지)
    // ══════════════════════════════════════════════════════════
    // 0 : 대기
    // 1 : 전진
    // 2 : 좌회전
    // 3 : 우회전
    //
    // BehaviorParameters → Actions → Discrete Branches : 1
    // Branch 0 Size : 4
    // ══════════════════════════════════════════════════════════
    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        int action = actionBuffers.DiscreteActions[0];

        switch (action)
        {
            case 1: // 전진
                transform.position += transform.forward * (_moveSpeed * Time.deltaTime);
                break;
            case 2: // 좌회전
                transform.Rotate(0f, -_rotationSpeed * Time.deltaTime, 0f);
                break;
            case 3: // 우회전
                transform.Rotate(0f, _rotationSpeed * Time.deltaTime, 0f);
                break;
        }

        // 매 스텝 패널티
        AddReward(-0.001f);

        // 가까워질수록 소량 보상
        if (_playerObject != null)
        {
            float dist = Vector3.Distance(transform.position, _playerObject.transform.position);
            AddReward(0.005f * Mathf.Clamp01(1f - dist / _maxDistance));
        }
    }

    // ══════════════════════════════════════════════════════════
    // 벽 충돌 패널티
    // ══════════════════════════════════════════════════════════
    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Wall")) return;

        AddReward(-0.05f);

        if (_renderer != null)
            _renderer.material.color = Color.yellow;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Wall")) return;

        // 벽에 붙어있는 동안 지속 패널티
        AddReward(-0.01f * Time.fixedDeltaTime);
        if (_renderer != null)
            _renderer.material.color = Color.blue;
    }

    // ══════════════════════════════════════════════════════════
    // 플레이어 접촉 감지 — 보상 + 에피소드 종료
    // ══════════════════════════════════════════════════════════
    private void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.CompareTag("Player")) return;
        GoalReached();
    }

    private void GoalReached()
    {
        AddReward(1f);
        _cumulativeReward = GetCumulativeReward(); // 보상 누적 초기화 (선택 사항)
        Debug.Log($"Episode {_currentEpisode} ended with reward: {_cumulativeReward:F2}");
        EndEpisode();

    }

    // ══════════════════════════════════════════════════════════
    // 휴리스틱 — 학습 전 수동 테스트
    // W : 전진(1)  A : 좌회전(2)  D : 우회전(3)
    // ══════════════════════════════════════════════════════════
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        d[0] = 0;

        if (Input.GetKey(KeyCode.W))      d[0] = 1;
        else if (Input.GetKey(KeyCode.A)) d[0] = 2;
        else if (Input.GetKey(KeyCode.D)) d[0] = 3;
    }
}
