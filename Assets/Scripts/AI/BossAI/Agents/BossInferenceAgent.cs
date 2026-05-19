using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// BossInferenceAgent — BAL-1 통합 에이전트
//
// BehaviorParameters:
//   VectorObservationSize    : 129
//   Discrete Branches        : [5] (이동만, 스킬은 BossAutoCastHelper 위임)
//   Max Step                 : 0 (에피소드 종료를 직접 관리, 720s)
//
// Action Branch 0:
//   0=idle  1=forward  2=turnLeft  3=turnRight  4=backward(×0.7)
public class BossInferenceAgent : Agent
{
    private const float BackwardSpeedScale = 0.7f;
    private const float EpisodeDuration    = 720f;
    private const int   MaxSkillSlots      = 5;

    [Header("Movement")]
    [SerializeField] private float _moveSpeed     = 8.4f;
    [SerializeField] private float _rotationSpeed = 540f;

    [Header("Phase Multipliers (§7)")]
    [SerializeField] private float[] _phaseSpeedScale    = { 1f, 1f, 1.1f, 1.2f };
    [SerializeField] private float[] _phaseCooldownScale = { 1f, 0.85f, 0.7f, 0.5f };
    [SerializeField] private float[] _phaseDamageScale   = { 1f, 1.08f, 1.16f, 1.25f };

    [Header("Spawn")]
    [SerializeField] private Transform       _bossSpawnPoint;
    [SerializeField] private GameObject      _p1Object;
    [SerializeField] private GameObject      _p2Object;
    [SerializeField] private List<Transform> _playerSpawnPoints;

    [Header("References")]
    [SerializeField] private BossController           _bossController;
    [SerializeField] private BossObservationCollector  _collector;
    [SerializeField] private TrainingSkillManager      _trainingSkillManager;
    [SerializeField] private SkillManager              _skillManager;
    [SerializeField] private SkillExecutor             _skillExecutor;
    [SerializeField] private BossAutoCastHelper        _autoCastHelper;
    [SerializeField] private PlayerBiasTracker         _biasTracker;

    [Header("Player Skill Assign")]
    [SerializeField] private TrainingSkillManager _p1TrainingSkillMgr;
    [SerializeField] private TrainingSkillManager _p2TrainingSkillMgr;

    [Header("Skill Pools")]
    [SerializeField] private SkillPoolSO[] _bossSkillPools;
    [SerializeField] private SkillPoolSO[] _playerSkillPools;

    [Header("Player Behavior Profiles")]
    [SerializeField] private PlayerProfile[] _archetypeProfiles;

    [Header("Logging")]
    [SerializeField] private int _statsLogInterval = 50;

    [System.Serializable]
    public struct PlayerProfile
    {
        public string ArchetypeName;
        public SkillPoolSO SkillPool;
        public Unity.Behavior.BehaviorGraph MoveGraph;
        [Header("이동 파라미터")]
        public float DangerRange;
        public float OptimalMin;
        public float OptimalMax;
        public float FleeDistance;
        public float FlankRadius;
        public float StrafeRadius;
        public float StrafeAngleStep;
        public float MinSpacing;
        public float AttackRangeMin;
        public float AttackRangeMax;
        public float SafeRangeMin;
        public float SafeRangeMax;
        public float MinFlankAngle;
    }

    // 런타임 상태
    private StatManager  _bossStatManager;
    private StatManager  _p1StatManager;
    private StatManager  _p2StatManager;

    private ProjectilePool     _projectilePool;
    private PersistentAreaPool _areaPool;

    private float _episodeStartTime;
    private int   _currentEpisode;
    private int   _lastPhaseRewarded;
    private float _prevBossHp;
    private float _prevP1Hp;
    private float _prevP2Hp;
    private float _bossTravelDist;
    private string _endReason;

    private int _lastPhaseApplied = -1;
    private int _p1ArchIdx;
    private int _p2ArchIdx;

    // CSV
    private string _csvPath;
    private string _behaviorName;

    private readonly Dictionary<string, MatchRecord> _matchupStats = new();
    private int _actIdle, _actFwd, _actLeft, _actRight, _actBack;
    private int _behavFrames;

    private class MatchRecord
    {
        public int Wins, Total;
        public float TotalDuration;
        public float WinRate => Total > 0 ? (float)Wins / Total * 100f : 0f;
    }

    // ══════════════════════════════════════════════════════════
    // 초기화
    // ══════════════════════════════════════════════════════════

    public override void Initialize()
    {
        if (_bossController == null) _bossController = GetComponent<BossController>();
        if (_collector == null)      _collector      = GetComponent<BossObservationCollector>();
        if (_trainingSkillManager == null) _trainingSkillManager = GetComponent<TrainingSkillManager>();
        if (_skillManager == null)   _skillManager   = GetComponent<SkillManager>();
        if (_skillExecutor == null)  _skillExecutor   = GetComponent<SkillExecutor>();
        if (_autoCastHelper == null) _autoCastHelper  = GetComponent<BossAutoCastHelper>();
        if (_biasTracker == null)    _biasTracker     = GetComponent<PlayerBiasTracker>();

        _bossStatManager = _bossController != null ? _bossController.StatMgr : null;

        _projectilePool = FindAnyObjectByType<ProjectilePool>();
        _areaPool       = FindAnyObjectByType<PersistentAreaPool>();

        if (_bossController != null) _bossController.TrainingMode = true;
        if (_skillManager != null)   _skillManager.SetAutoCast(false);

        if (_trainingSkillManager != null)
            _trainingSkillManager.SetUnlockConfig(MaxSkillSlots, MaxSkillSlots);

        // Player: initial 1 + 3 drafts @ 180s = max 4 (핸드오프 §7)
        if (_p1TrainingSkillMgr != null)
            _p1TrainingSkillMgr.SetUnlockConfig(1, 4, 180f);
        if (_p2TrainingSkillMgr != null)
            _p2TrainingSkillMgr.SetUnlockConfig(1, 4, 180f);

        var bp = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        _behaviorName = bp != null ? bp.BehaviorName : "BossInference";

        InitCsv();
    }

    // ══════════════════════════════════════════════════════════
    // 에피소드 시작
    // ══════════════════════════════════════════════════════════

    public override void OnEpisodeBegin()
    {
        CleanupPools();
        PickArchetypes();
        AssignPools();
        SpawnAll();
        ResetState();

        if (_autoCastHelper != null)
        {
            _autoCastHelper.Initialize(_p1Object, _p2Object);
            _autoCastHelper.SetEnabled(true);
        }

        _currentEpisode++;
    }

    private void CleanupPools()
    {
        if (_projectilePool != null) _projectilePool.ReturnAll();
        if (_areaPool != null)       _areaPool.ReturnAll();
    }

    private void PickArchetypes()
    {
        int count = (_archetypeProfiles != null) ? _archetypeProfiles.Length : 0;
        _p1ArchIdx = count > 0 ? Random.Range(0, count) : 0;
        _p2ArchIdx = count > 0 ? Random.Range(0, count) : 0;
    }

    private void AssignPools()
    {
        if (_bossSkillPools != null && _bossSkillPools.Length > 0 && _trainingSkillManager != null)
        {
            var pool = _bossSkillPools[Random.Range(0, _bossSkillPools.Length)];
            _trainingSkillManager.SetSkillPool(pool);
        }

        if (_archetypeProfiles != null && _archetypeProfiles.Length > 0)
        {
            AssignPlayerPool(_p1TrainingSkillMgr, _p1Object, _p1ArchIdx);
            AssignPlayerPool(_p2TrainingSkillMgr, _p2Object, _p2ArchIdx);
        }
    }

    private void AssignPlayerPool(TrainingSkillManager tsMgr, GameObject player, int archIdx)
    {
        if (_archetypeProfiles == null || archIdx < 0 || archIdx >= _archetypeProfiles.Length) return;
        var profile = _archetypeProfiles[archIdx];

        if (tsMgr != null && profile.SkillPool != null)
            tsMgr.SetSkillPool(profile.SkillPool);

        SwapPlayerGraph(player, profile);
    }

    private void SwapPlayerGraph(GameObject player, PlayerProfile profile)
    {
        if (player == null || profile.MoveGraph == null) return;

        var agent = player.GetComponent<Unity.Behavior.BehaviorGraphAgent>();
        if (agent == null) return;

        agent.Graph = profile.MoveGraph;
        agent.Init();

        var nav = player.GetComponent<UnityEngine.AI.NavMeshAgent>();
        agent.SetVariableValue("Boss", gameObject);
        agent.SetVariableValue("Self", player);
        agent.SetVariableValue("Agent", nav);

        GameObject ally = (player == _p1Object) ? _p2Object : _p1Object;
        agent.SetVariableValue("Ally", ally);

        agent.SetVariableValue("DangerRange", profile.DangerRange);
        agent.SetVariableValue("OptimalMin", profile.OptimalMin);
        agent.SetVariableValue("OptimalMax", profile.OptimalMax);
        agent.SetVariableValue("FleeDistance", profile.FleeDistance);
        agent.SetVariableValue("FlankRadius", profile.FlankRadius);
        agent.SetVariableValue("StrafeRadius", profile.StrafeRadius);
        agent.SetVariableValue("StrafeAngleStep", profile.StrafeAngleStep);
        agent.SetVariableValue("MinSpacing", profile.MinSpacing);
        agent.SetVariableValue("AttackRangeMin", profile.AttackRangeMin);
        agent.SetVariableValue("AttackRangeMax", profile.AttackRangeMax);
        agent.SetVariableValue("SafeRangeMin", profile.SafeRangeMin);
        agent.SetVariableValue("SafeRangeMax", profile.SafeRangeMax);
        agent.SetVariableValue("MinFlankAngle", profile.MinFlankAngle);

        agent.Restart();
    }

    private void SpawnAll()
    {
        if (_bossSpawnPoint != null)
            transform.SetPositionAndRotation(_bossSpawnPoint.position, _bossSpawnPoint.rotation);
        else
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

        if (_p1Object != null && _p2Object != null &&
            _playerSpawnPoints != null && _playerSpawnPoints.Count >= 2)
        {
            int idx1 = Random.Range(0, _playerSpawnPoints.Count);
            int idx2;
            do { idx2 = Random.Range(0, _playerSpawnPoints.Count); } while (idx2 == idx1);

            PlaceOnSpawn(_p1Object, _playerSpawnPoints[idx1]);
            PlaceOnSpawn(_p2Object, _playerSpawnPoints[idx2]);
        }

        if (_collector != null) _collector.SetPlayers(_p1Object, _p2Object);
    }

    private void PlaceOnSpawn(GameObject target, Transform spawn)
    {
        if (target == null || spawn == null) return;

        foreach (var mb in target.GetComponents<MonoBehaviour>())
            mb.StopAllCoroutines();

        if (target.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (target.TryGetComponent(out UnityEngine.AI.NavMeshAgent nav) && nav.enabled)
        {
            nav.Warp(spawn.position);
            target.transform.rotation = spawn.rotation;
            nav.ResetPath();
            nav.velocity = Vector3.zero;
        }
        else
        {
            target.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        }
    }

    private void ResetState()
    {
        if (_bossController != null) _bossController.ResetPhaseForTraining();
        _bossStatManager?.ResetForTraining();
        _bossController?.StateMgr?.ForceReset();

        ResetPlayer(_p1Object);
        ResetPlayer(_p2Object);

        if (_skillExecutor != null) _skillExecutor.ResetAll();
        if (_trainingSkillManager != null) _trainingSkillManager.ResetForEpisode();

        if (_p1TrainingSkillMgr != null) _p1TrainingSkillMgr.ResetForEpisode();
        if (_p2TrainingSkillMgr != null) _p2TrainingSkillMgr.ResetForEpisode();

        if (_biasTracker != null) _biasTracker.ResetAll();

        _p1StatManager = _p1Object != null ? _p1Object.GetComponent<StatManager>() : null;
        _p2StatManager = _p2Object != null ? _p2Object.GetComponent<StatManager>() : null;

        _episodeStartTime  = Time.time;
        _lastPhaseRewarded = 0;
        _lastPhaseApplied  = -1;
        ApplyPhaseMultipliers();
        _bossTravelDist    = 0f;
        _endReason         = "Unknown";

        _prevBossHp = 1f;
        _prevP1Hp   = _p1StatManager != null ? _p1StatManager.GetHPPercent() : 0f;
        _prevP2Hp   = _p2StatManager != null ? _p2StatManager.GetHPPercent() : 0f;

        _actIdle = _actFwd = _actLeft = _actRight = _actBack = 0;
        _behavFrames = 0;
    }

    private void ResetPlayer(GameObject player)
    {
        if (player == null) return;
        if (player.TryGetComponent(out StatManager stat))  stat.ResetForTraining();
        if (player.TryGetComponent(out StateManager state)) state.ForceReset();
        if (player.TryGetComponent(out SkillExecutor exec)) exec.ResetAll();

        if (player.TryGetComponent(out UnityEngine.AI.NavMeshAgent nav))
            nav.isStopped = false;
        if (player.TryGetComponent(out Rigidbody rb))
            rb.isKinematic = false;
    }

    // ══════════════════════════════════════════════════════════
    // 관측 (129ch)
    // ══════════════════════════════════════════════════════════

    public override void CollectObservations(VectorSensor sensor)
    {
        if (_collector == null)
        {
            sensor.AddObservation(new float[BossObservationCollector.FullObsSize]);
            return;
        }

        _collector.CollectFull129(sensor);
    }

    // ══════════════════════════════════════════════════════════
    // 행동 — Branch 0 [5] 이동만
    // ══════════════════════════════════════════════════════════

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        ApplyPhaseMultipliers();

        if (_trainingSkillManager != null) _trainingSkillManager.Tick();

        bool p1Alive = _p1StatManager != null && _p1StatManager.IsAlive;
        bool p2Alive = _p2StatManager != null && _p2StatManager.IsAlive;
        if (_p1TrainingSkillMgr != null && p1Alive) _p1TrainingSkillMgr.Tick();
        if (_p2TrainingSkillMgr != null && p2Alive) _p2TrainingSkillMgr.Tick();

        int move = actionBuffers.DiscreteActions[0];

        bool busy = _bossController != null && _bossController.IsCasting;
        if (!busy)
        {
            float speedScale = GetPhaseSpeedScale();
            float dt = Time.deltaTime;

            switch (move)
            {
                case 1:
                    float fwdDist = _moveSpeed * speedScale * dt;
                    transform.position += transform.forward * fwdDist;
                    _bossTravelDist += fwdDist;
                    break;
                case 2:
                    transform.Rotate(0f, -_rotationSpeed * dt, 0f);
                    break;
                case 3:
                    transform.Rotate(0f, _rotationSpeed * dt, 0f);
                    break;
                case 4:
                    float backDist = _moveSpeed * BackwardSpeedScale * speedScale * dt;
                    transform.position -= transform.forward * backDist;
                    _bossTravelDist += backDist;
                    break;
            }
        }

        TrackAction(move);
        bool isMoving = move == 1 || move == 4;
        _bossController.StateMgr?.NotifyMovementInput(isMoving);

        ApplyStepRewards();
        CheckTermination();
    }

    private float GetPhaseSpeedScale()
    {
        int phase = _bossController != null ? _bossController.CurrentPhase : 0;
        if (_phaseSpeedScale == null || _phaseSpeedScale.Length == 0) return 1f;
        int idx = Mathf.Clamp(phase, 0, _phaseSpeedScale.Length - 1);
        return _phaseSpeedScale[idx];
    }

    private void ApplyPhaseMultipliers()
    {
        int phase = _bossController != null ? _bossController.CurrentPhase : 0;
        if (phase == _lastPhaseApplied) return;
        _lastPhaseApplied = phase;

        if (_skillExecutor != null && _phaseCooldownScale != null && _phaseCooldownScale.Length > 0)
        {
            int idx = Mathf.Clamp(phase, 0, _phaseCooldownScale.Length - 1);
            _skillExecutor.CooldownMultiplier = _phaseCooldownScale[idx];
        }

        if (_bossStatManager != null && _phaseDamageScale != null && _phaseDamageScale.Length > 0)
        {
            int idx = Mathf.Clamp(phase, 0, _phaseDamageScale.Length - 1);
            _bossStatManager.SetPhaseDamageMultiplier(_phaseDamageScale[idx]);
        }
    }

    private void TrackAction(int move)
    {
        _behavFrames++;
        switch (move)
        {
            case 0: _actIdle++;  break;
            case 1: _actFwd++;   break;
            case 2: _actLeft++;  break;
            case 3: _actRight++; break;
            case 4: _actBack++;  break;
        }
    }

    // ══════════════════════════════════════════════════════════
    // 액션 마스크 — IsBusy 시 이동 1~4 비활성 (idle만 허용)
    // ══════════════════════════════════════════════════════════

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (_bossController != null && _bossController.IsCasting)
        {
            for (int i = 1; i <= 4; i++)
                actionMask.SetActionEnabled(0, i, false);
        }
    }

    // ══════════════════════════════════════════════════════════
    // Heuristic — W/A/S/D 매핑
    // ══════════════════════════════════════════════════════════

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        d[0] = 0;

        if (Input.GetKey(KeyCode.W))      d[0] = 1;
        else if (Input.GetKey(KeyCode.A)) d[0] = 2;
        else if (Input.GetKey(KeyCode.D)) d[0] = 3;
        else if (Input.GetKey(KeyCode.S)) d[0] = 4;
    }

    // ══════════════════════════════════════════════════════════
    // 보상 (7 신호)
    // ══════════════════════════════════════════════════════════

    private void ApplyStepRewards()
    {
        // Step penalty
        AddReward(-0.0001f);

        // Damage dealt to players
        float p1Hp = _p1StatManager != null ? _p1StatManager.GetHPPercent() : 0f;
        float p2Hp = _p2StatManager != null ? _p2StatManager.GetHPPercent() : 0f;
        float p1MaxHp = _p1StatManager != null ? _p1StatManager.GetMaxHP() : 1f;
        float p2MaxHp = _p2StatManager != null ? _p2StatManager.GetMaxHP() : 1f;

        float p1Drop = Mathf.Max(0f, _prevP1Hp - p1Hp) * p1MaxHp;
        float p2Drop = Mathf.Max(0f, _prevP2Hp - p2Hp) * p2MaxHp;
        float dmgDealt = p1Drop + p2Drop;
        if (dmgDealt > 0f) AddReward(0.001f * dmgDealt);

        // Damage taken by boss
        float bossHp = _bossStatManager != null ? _bossStatManager.GetHPPercent() : 0f;
        float bossMaxHp = _bossStatManager != null ? _bossStatManager.GetMaxHP() : 1f;
        float bossDrop = Mathf.Max(0f, _prevBossHp - bossHp) * bossMaxHp;
        if (bossDrop > 0f) AddReward(-0.0005f * bossDrop);

        // Phase progress
        int phase = _bossController != null ? _bossController.CurrentPhase : 0;
        if (phase > _lastPhaseRewarded)
        {
            int steps = phase - _lastPhaseRewarded;
            AddReward(0.05f * steps);
            _lastPhaseRewarded = phase;
        }

        _prevBossHp = bossHp;
        _prevP1Hp   = p1Hp;
        _prevP2Hp   = p2Hp;
    }

    // ══════════════════════════════════════════════════════════
    // 종료 조건
    // ══════════════════════════════════════════════════════════

    private void CheckTermination()
    {
        bool bossAlive = _bossStatManager != null && _bossStatManager.IsAlive;
        bool p1Alive   = _p1StatManager != null && _p1StatManager.IsAlive;
        bool p2Alive   = _p2StatManager != null && _p2StatManager.IsAlive;

        if (!bossAlive)
        {
            _endReason = "BossLose";
            AddReward(-1f);
            LogEpisodeCsv(false);
            EndEpisode();
            return;
        }

        if (!p1Alive && !p2Alive)
        {
            _endReason = "BossWin";
            AddReward(1f);
            LogEpisodeCsv(true);
            EndEpisode();
            return;
        }

        if (Time.time - _episodeStartTime >= EpisodeDuration)
        {
            _endReason = "Timeout";
            AddReward(-0.3f);
            LogEpisodeCsv(false);
            EndEpisode();
            return;
        }

        if (transform.position.y < -5f)
        {
            _endReason = "OutOfBounds";
            AddReward(-1f);
            LogEpisodeCsv(false);
            EndEpisode();
        }
    }

    // ══════════════════════════════════════════════════════════
    // CSV 로깅
    // ══════════════════════════════════════════════════════════

    private void InitCsv()
    {
        int workerIdx = 0;
        var academy = Academy.Instance;
        if (academy != null)
        {
            var envParams = academy.EnvironmentParameters;
            workerIdx = (int)envParams.GetWithDefault("worker_id", 0f);
        }
        string suffix = workerIdx > 0 ? $"_{_behaviorName}_w{workerIdx}" : $"_{_behaviorName}";
        _csvPath = System.IO.Path.Combine(Application.dataPath, "..", $"results/episodes{suffix}.csv");
        string dir = System.IO.Path.GetDirectoryName(_csvPath);
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        string header = "episode_id,p1_archetype,p2_archetype,outcome,duration_sec,boss_hp_remaining," +
                         "total_damage_to_boss,total_damage_to_players,p1_skills_unlocked,p2_skills_unlocked," +
                         "phase_reached,cumulative_reward,boss_travel_dist,idle_ratio,fwd_ratio,rot_ratio,back_ratio";
        if (!System.IO.File.Exists(_csvPath))
        {
            System.IO.File.WriteAllText(_csvPath, header + "\n");
        }
        else
        {
            string sep = $"\n# === Session {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n{header}\n";
            try { System.IO.File.AppendAllText(_csvPath, sep); } catch { }
        }
    }

    private void LogEpisodeCsv(bool bossWon)
    {
        float duration = Time.time - _episodeStartTime;
        float bossHpLeft = _bossStatManager != null ? _bossStatManager.GetHPPercent() : 0f;
        float bossMaxHp  = _bossStatManager != null ? _bossStatManager.GetMaxHP() : 1f;
        float p1MaxHp    = _p1StatManager != null ? _p1StatManager.GetMaxHP() : 1f;
        float p2MaxHp    = _p2StatManager != null ? _p2StatManager.GetMaxHP() : 1f;
        float p1HpLeft   = _p1StatManager != null ? _p1StatManager.GetHPPercent() : 0f;
        float p2HpLeft   = _p2StatManager != null ? _p2StatManager.GetHPPercent() : 0f;

        float dmgToBoss    = (1f - bossHpLeft) * bossMaxHp;
        float dmgToPlayers = (1f - p1HpLeft) * p1MaxHp + (1f - p2HpLeft) * p2MaxHp;

        int p1Unlocked = _p1TrainingSkillMgr != null ? _p1TrainingSkillMgr.UnlockedCount : 0;
        int p2Unlocked = _p2TrainingSkillMgr != null ? _p2TrainingSkillMgr.UnlockedCount : 0;

        string p1Arch = GetArchName(_p1ArchIdx);
        string p2Arch = GetArchName(_p2ArchIdx);
        string outcome = bossWon ? "BossWin" : _endReason;

        float idleR = _behavFrames > 0 ? (float)_actIdle / _behavFrames : 0f;
        float fwdR  = _behavFrames > 0 ? (float)_actFwd  / _behavFrames : 0f;
        float rotR  = _behavFrames > 0 ? (float)(_actLeft + _actRight) / _behavFrames : 0f;
        float backR = _behavFrames > 0 ? (float)_actBack  / _behavFrames : 0f;

        string line = $"{_currentEpisode},{p1Arch},{p2Arch},{outcome},{duration:F1},{bossHpLeft:F3}," +
                      $"{dmgToBoss:F0},{dmgToPlayers:F0},{p1Unlocked},{p2Unlocked}," +
                      $"{_bossController.CurrentPhase},{GetCumulativeReward():F3},{_bossTravelDist:F1}," +
                      $"{idleR:F3},{fwdR:F3},{rotR:F3},{backR:F3}";
        try { System.IO.File.AppendAllText(_csvPath, line + "\n"); } catch { }

        string key = $"{p1Arch}+{p2Arch}";
        if (!_matchupStats.TryGetValue(key, out var rec))
        {
            rec = new MatchRecord();
            _matchupStats[key] = rec;
        }
        if (bossWon) rec.Wins++;
        rec.Total++;
        rec.TotalDuration += duration;

        if (_currentEpisode > 0 && _currentEpisode % _statsLogInterval == 0)
            LogStats();
    }

    private string GetArchName(int idx)
    {
        if (_archetypeProfiles == null || idx < 0 || idx >= _archetypeProfiles.Length) return "Unknown";
        return _archetypeProfiles[idx].ArchetypeName;
    }

    private void LogStats()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BossInference] EP#{_currentEpisode} ════════════════════");
        foreach (var kvp in _matchupStats)
            sb.AppendLine($"  {kvp.Key,-30} | 승률:{kvp.Value.WinRate,5:F1}% ({kvp.Value.Wins}/{kvp.Value.Total})");
        Debug.Log(sb.ToString());
    }
}
