using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// ══════════════════════════════════════════════════════════════════════
// Phase 2 — DualTarget
// BasicMove(Phase1) 학습 모델 위에 이어서 학습
// 두 명의 플레이어를 동시에 관측하고 "둘 다 접촉"을 목표로 유도
//
// BehaviorParameters 설정:
//   Behavior Name           : DualTarget
//   Vector Observation Size : 11
//   Discrete Branches       : 1  (Branch 0 Size : 4)
//   Max Step                : 5000 (권장)
//
// 학습 명령 예시:
//   mlagents-learn DualTarget_config.yaml \
//     --initialize-from=<BasicMove_run_id> --run-id=DualTarget_001
// ══════════════════════════════════════════════════════════════════════
[System.Obsolete("BossInferenceAgent 사용")]
public class DualTargetAgent : Agent
{
    [Header("이동")]
    [SerializeField] private float _moveSpeed     = 8.4f;
    [SerializeField] private float _rotationSpeed = 540f;

    [Header("관측 설정")]
    [SerializeField] private float _maxDistance = 55f;

    [Header("스폰 설정")]
    [SerializeField] private Transform       _bossSpawnPoint;
    [SerializeField] private GameObject      _p1Object;
    [SerializeField] private GameObject      _p2Object;
    [SerializeField] private List<Transform> _playerSpawnPoints;

    [Header("보상 튜닝")]
    [SerializeField] private float _doubleTouchWindow   = 3.0f;    // 보너스 지급 윈도우

    [Header("종료 조건 (근접 — 접촉 대신)")]
    [SerializeField] private float _proximityRadius       = 5.0f;   // 이 거리 이내로 들어오면 "접근 성공" 간주
    [SerializeField] private float _episodeMaxDurationSec = 30.0f;  // 에피소드 절대 최대 시간 (필수 종료)
    [SerializeField] private float _noTouchPenalty        = -0.5f;  // 시간 종료 시 둘 다 미근접
    [SerializeField] private float _partialTouchPenalty   = -0.2f;  // 시간 종료 시 한쪽만 근접

    [Header("종료 조건 (위치)")]
    [SerializeField] private float _outOfBoundsY         = -5f;     // 이 Y 아래로 떨어지면 실패
    [SerializeField] private float _outOfBoundsPenalty   = -1.0f;

    [Header("종료 조건 (보너스)")]
    [SerializeField] private float _fastDoubleTouchBonus = 0.5f;    // 윈도우 내 양쪽 근접 시 추가

    [Header("Phase 2b — 추격 품질 향상 (순차 추격 기반)")]
    [SerializeField] private float _wStepPenalty       = 0.005f;  // 시간 압박 — 느긋함 방지
    [SerializeField] private float _wProgress          = 0.50f;   // activeTarget 거리 감소 (메인)
    [SerializeField] private float _wAlign             = 0.003f;  // active 타겟을 바라보며 접근 보상
    [SerializeField] private float _wIdlePenalty       = 0.005f;  // 정지/회전만 시 패널티
    [SerializeField] private float _minMoveDelta       = 0.05f;   // 정지 감지 임계 (1프레임 위치변화)
    [SerializeField] private float _alignDotThreshold  = 0.7f;    // 정렬 보상 발동 임계 (dot > 이 값)

    private Renderer _renderer;
    private int      _currentEpisode   = 0;
    private float    _cumulativeReward = 0f;

    private bool  _p1Touched          = false;
    private bool  _p2Touched          = false;
    private float _p1TouchTime        = -1f;
    private float _p2TouchTime        = -1f;

    // 거리 감소 진행 보상용 — 이전 스텝 타겟 거리
    private float _prevTargetDist     = -1f;

    // 정지 패널티용 — 이전 스텝 보스 위치
    private Vector3 _prevBossPos      = Vector3.zero;
    private bool    _prevPosValid     = false;

    // 에피소드 시작 시각 — 시간 진행도 계산 / 컷오프 판정용
    private float _episodeStartTime   = 0f;

    // 성공 카운터 — 양쪽 접촉으로 종료된 횟수 누적 (Inspector 표시용 + 디버그)
    private int _successCount      = 0;
    private int _fastSuccessCount  = 0;
    private int _slowSuccessCount  = 0;

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
        ResetEpisodeState();
        Debug.Log($"[DualTarget] Episode {_currentEpisode} ended with reward: {_cumulativeReward:F2}");
        _currentEpisode++;
    }

    private void ResetEpisodeState()
    {
        _p1Touched = false;
        _p2Touched = false;
        _p1TouchTime = -1f;
        _p2TouchTime = -1f;
        _prevTargetDist = -1f;
        _prevPosValid = false;
        _prevBossPos = transform.position;
        _episodeStartTime = Time.time;
    }

    // ══════════════════════════════════════════════════════════
    // activeTarget 판정 — 관측/보상 모두에서 동일 사용
    //   - 안 만진 쪽 우선
    //   - 둘 다 미접촉이면 더 가까운 쪽
    // ══════════════════════════════════════════════════════════
    private bool ActiveIsP1(float distP1, float distP2)
    {
        if (_p1Touched && !_p2Touched) return false;
        if (_p2Touched && !_p1Touched) return true;
        return distP1 <= distP2;
    }

    private void SpawnObjects()
    {
        // 보스 초기화 — ML 이동은 transform 직접 조작이므로 그대로 사용
        if (_bossSpawnPoint != null)
            transform.SetPositionAndRotation(_bossSpawnPoint.position, _bossSpawnPoint.rotation);
        else
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

        // 플레이어: 스폰 리스트에서 서로 다른 2개 인덱스 랜덤 선택
        if (_p1Object == null || _p2Object == null) return;
        if (_playerSpawnPoints == null || _playerSpawnPoints.Count < 2)
        {
            Debug.LogWarning("[DualTarget] _playerSpawnPoints 2개 미만 — 스폰 건너뜀");
            return;
        }

        int idx1 = Random.Range(0, _playerSpawnPoints.Count);
        int idx2;
        do { idx2 = Random.Range(0, _playerSpawnPoints.Count); } while (idx2 == idx1);

        PlaceOnSpawn(_p1Object, _playerSpawnPoints[idx1]);
        PlaceOnSpawn(_p2Object, _playerSpawnPoints[idx2]);
    }

    // 플레이어 배치 — NavMeshAgent 가 있으면 Warp 로 NavMesh 상태 재동기화
    private void PlaceOnSpawn(GameObject target, Transform spawn)
    {
        if (target == null || spawn == null) return;

        if (target.TryGetComponent(out NavMeshAgent nav) && nav.enabled)
        {
            nav.Warp(spawn.position);                 // NavMesh 재스냅
            target.transform.rotation = spawn.rotation;
            nav.ResetPath();                          // 이전 에피소드 경로 제거
            nav.velocity = Vector3.zero;
        }
        else
        {
            target.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        }
    }

    // ══════════════════════════════════════════════════════════
    // 관측 (11개) — Phase 2 재설계 (activeTarget 우선 정렬)
    //   고정 P1/P2 슬롯 → 진동 학습 유발 → 정렬된 슬롯으로 변경
    //   "active" 슬롯은 항상 추격 대상, "secondary" 슬롯은 다음 대상
    //
    // [0~1]  dirToActive.x/z       — 추격 대상 방향
    // [2]    distToActive          — 추격 대상 거리 (정규화)
    // [3]    dotForwardActive      — 전방↔추격대상 정렬도
    // [4~5]  dirToSecondary.x/z    — 다음 대상 방향
    // [6]    distToSecondary       — 다음 대상 거리 (정규화)
    // [7]    dotForwardSecondary   — 전방↔다음대상 정렬도
    // [8]    distP1P2              — 두 플레이어 사이 거리 (전략 정보)
    // [9]    secondaryTouched      — 다음 대상 이미 접촉 여부 (0/1)
    // [10]   timeProgress          — 에피소드 시간 진행도 (0~1, 긴박감)
    // ══════════════════════════════════════════════════════════
    public override void CollectObservations(VectorSensor sensor)
    {
        if (_p1Object == null || _p2Object == null)
        {
            sensor.AddObservation(new float[11]);
            return;
        }

        Vector3 bossPos = transform.position;
        Vector3 fwd     = transform.forward;

        Vector3 toP1   = _p1Object.transform.position - bossPos;
        Vector3 toP2   = _p2Object.transform.position - bossPos;
        Vector3 p1ToP2 = _p2Object.transform.position - _p1Object.transform.position;

        float distP1 = toP1.magnitude;
        float distP2 = toP2.magnitude;
        float distPP = p1ToP2.magnitude;

        Vector3 dirP1 = distP1 > 0.001f ? toP1.normalized : Vector3.forward;
        Vector3 dirP2 = distP2 > 0.001f ? toP2.normalized : Vector3.forward;

        // ── active/secondary 정렬 ─────────────────────────
        bool activeIsP1 = ActiveIsP1(distP1, distP2);

        Vector3 dirActive    = activeIsP1 ? dirP1  : dirP2;
        Vector3 dirSecondary = activeIsP1 ? dirP2  : dirP1;
        float   distActive    = activeIsP1 ? distP1 : distP2;
        float   distSecondary = activeIsP1 ? distP2 : distP1;
        bool    secondaryTouched = activeIsP1 ? _p2Touched : _p1Touched;

        // [0~1]
        sensor.AddObservation(dirActive.x);
        sensor.AddObservation(dirActive.z);
        // [2]
        sensor.AddObservation(Mathf.Clamp01(distActive / _maxDistance));
        // [3]
        sensor.AddObservation(Vector3.Dot(fwd, dirActive));
        // [4~5]
        sensor.AddObservation(dirSecondary.x);
        sensor.AddObservation(dirSecondary.z);
        // [6]
        sensor.AddObservation(Mathf.Clamp01(distSecondary / _maxDistance));
        // [7]
        sensor.AddObservation(Vector3.Dot(fwd, dirSecondary));
        // [8]
        sensor.AddObservation(Mathf.Clamp01(distPP / _maxDistance));
        // [9]
        sensor.AddObservation(secondaryTouched ? 1f : 0f);
        // [10]
        sensor.AddObservation(Mathf.Clamp01((Time.time - _episodeStartTime) / _episodeMaxDurationSec));
    }

    // ══════════════════════════════════════════════════════════
    // 행동 (Phase 1과 동일 — 이동 4)
    // 0 : 대기  1 : 전진  2 : 좌회전  3 : 우회전
    // ══════════════════════════════════════════════════════════
    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        int action = actionBuffers.DiscreteActions[0];

        switch (action)
        {
            case 1: transform.position += transform.forward * (_moveSpeed * Time.deltaTime); break;
            case 2: transform.Rotate(0f, -_rotationSpeed * Time.deltaTime, 0f); break;
            case 3: transform.Rotate(0f,  _rotationSpeed * Time.deltaTime, 0f); break;
        }

        ApplyStepRewards();
        CheckTerminationConditions();
    }

    // ══════════════════════════════════════════════════════════
    // 종료 조건 검사 — 매 스텝 호출
    //   1) 맵 밖 이탈              → _outOfBoundsPenalty,  실패 종료
    //   2) 에피소드 시간 하드 컷오프 → 접촉 상태별 차등 패널티, 강제 종료
    //   (성공 종료는 ApplyStepRewards 의 CheckProximityTouch 에서 처리)
    // ══════════════════════════════════════════════════════════
    private void CheckTerminationConditions()
    {
        // 1) 맵 밖 이탈 (Y축 낙하)
        if (transform.position.y < _outOfBoundsY)
        {
            AddReward(_outOfBoundsPenalty);
            _cumulativeReward = GetCumulativeReward();
            if (_renderer != null) _renderer.material.color = Color.gray;
            Debug.Log($"[DualTarget] ❌ 맵 이탈 실패 reward={_cumulativeReward:F2}");
            EndEpisode();
            return;
        }

        // 2) 에피소드 시간 하드 컷오프 — 접촉 여부 무관 강제 종료
        if (Time.time - _episodeStartTime > _episodeMaxDurationSec)
        {
            float penalty;
            string label;
            Color  c;
            if (!_p1Touched && !_p2Touched)
            {
                penalty = _noTouchPenalty;
                label   = "둘 다 미근접";
                c       = Color.magenta;
            }
            else if (_p1Touched ^ _p2Touched)
            {
                penalty = _partialTouchPenalty;
                label   = "한쪽만 근접";
                c       = Color.black;
            }
            else
            {
                // 둘 다 근접인데 CheckProximityTouch 가 EndEpisode 안 한 비정상 — 안전 종료
                penalty = 0f;
                label   = "양쪽 근접(비정상)";
                c       = Color.white;
            }

            AddReward(penalty);
            _cumulativeReward = GetCumulativeReward();
            if (_renderer != null) _renderer.material.color = c;
            Debug.Log($"[DualTarget] ⏱ 시간 종료({label}) reward={_cumulativeReward:F2}");
            EndEpisode();
        }
    }

    // ══════════════════════════════════════════════════════════
    // 스텝 보상 — Phase 2b: 순차 추격 + 추격 품질
    //
    //   0) 근접 판정       : distP < _proximityRadius → touched 처리
    //   1) 시간 압박       : −_wStepPenalty
    //   2) active 진행     : (이전거리 − 현재거리) × _wProgress
    //   3) 정렬 보상       : dot(forward, activeDir) > 임계 시 +_wAlign
    //   4) 정지 패널티     : 위치 변화 < _minMoveDelta 시 −_wIdlePenalty
    // ══════════════════════════════════════════════════════════
    private void ApplyStepRewards()
    {
        // ── 1) 시간 압박 ──────────────────────────────────
        AddReward(-_wStepPenalty);

        if (_p1Object == null || _p2Object == null) { _prevPosValid = false; return; }

        Vector3 bossPos = transform.position;
        Vector3 fwd     = transform.forward;
        Vector3 p1Pos   = _p1Object.transform.position;
        Vector3 p2Pos   = _p2Object.transform.position;

        float distP1 = Vector3.Distance(bossPos, p1Pos);
        float distP2 = Vector3.Distance(bossPos, p2Pos);

        // ── 0) 근접 판정 — 종료 조건이면 여기서 즉시 반환 ──
        if (CheckProximityTouch(distP1, distP2)) return;

        bool    activeIsP1 = ActiveIsP1(distP1, distP2);
        float   targetDist = activeIsP1 ? distP1 : distP2;
        Vector3 targetDir  = activeIsP1
            ? (p1Pos - bossPos).normalized
            : (p2Pos - bossPos).normalized;

        // ── 2) active 진행 보상 (메인 드라이버) ─────────
        if (_prevTargetDist > 0f)
        {
            float delta = _prevTargetDist - targetDist;
            AddReward(delta * _wProgress);
        }
        _prevTargetDist = targetDist;

        // ── 3) 정렬 보상 — 타겟을 바라보며 접근 ──────────
        if (Vector3.Dot(fwd, targetDir) > _alignDotThreshold)
            AddReward(_wAlign);

        // ── 4) 정지 패널티 — 가만히 있거나 회전만 하면 ──
        if (_prevPosValid)
        {
            float moveDelta = Vector3.Distance(bossPos, _prevBossPos);
            if (moveDelta < _minMoveDelta)
                AddReward(-_wIdlePenalty);
        }
        _prevBossPos  = bossPos;
        _prevPosValid = true;
    }

    // ══════════════════════════════════════════════════════════
    // 근접 판정 — trigger collision 대신 거리 기반 "접근 성공"
    //   양쪽 다 _proximityRadius 안에 한번씩 들어왔으면 성공 종료
    //   반환값: true = 에피소드 종료됨 (호출자는 즉시 return 해야 함)
    // ══════════════════════════════════════════════════════════
    private bool CheckProximityTouch(float distP1, float distP2)
    {
        float now = Time.time;

        if (!_p1Touched && distP1 < _proximityRadius)
        {
            _p1Touched = true;
            _p1TouchTime = now;
            AddReward(0.5f);
            _prevTargetDist = -1f;   // activeTarget 전환 — 진행 보상 점프 방지
        }
        if (!_p2Touched && distP2 < _proximityRadius)
        {
            _p2Touched = true;
            _p2TouchTime = now;
            AddReward(0.5f);
            _prevTargetDist = -1f;
        }

        if (!_p1Touched || !_p2Touched) return false;

        // 양쪽 모두 근접 → 성공 종료
        float gap    = Mathf.Abs(_p1TouchTime - _p2TouchTime);
        bool  isFast = gap <= _doubleTouchWindow;
        if (isFast) AddReward(_fastDoubleTouchBonus);

        _successCount++;
        if (isFast) _fastSuccessCount++;
        else        _slowSuccessCount++;

        _cumulativeReward = GetCumulativeReward();
        if (_renderer != null) _renderer.material.color = isFast ? Color.green : Color.cyan;
        Debug.Log($"[DualTarget] ✅ {(isFast ? "FAST " : "SLOW ")}근접 성공 #{_successCount} " +
                  $"(FAST={_fastSuccessCount} / SLOW={_slowSuccessCount}) " +
                  $"gap={gap:F2}s reward={_cumulativeReward:F2}");
        EndEpisode();
        return true;
    }

    // ══════════════════════════════════════════════════════════
    // 벽 충돌 패널티 (Phase 1 유지)
    // ══════════════════════════════════════════════════════════
    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Wall")) return;
        AddReward(-0.05f);
        if (_renderer != null) _renderer.material.color = Color.yellow;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Wall")) return;
        AddReward(-0.01f * Time.fixedDeltaTime);
        if (_renderer != null) _renderer.material.color = Color.blue;
    }

    // ══════════════════════════════════════════════════════════
    // 휴리스틱 — W: 전진  A: 좌회전  D: 우회전
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
