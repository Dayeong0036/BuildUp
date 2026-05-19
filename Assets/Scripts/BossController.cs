using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// ══════════════════════════════════════════════════════════════════
// BossController — Stage 5 리팩터
//
// 플레이어와 동일하게 StatManager 로 스탯/피해/상태 전부 위임.
// 보스는 실드 / 패링이 없으므로 ShieldMax=0, ParryWindow=0 으로 Initialize.
//
// 이전 monolith StatsManager 의존 제거:
//   statsManager (StatsManager) → _statManager (StatManager)
//   GetBossHP/GetBossMaxHP/GetBossHPPercent → GetHP/GetMaxHP/GetHPPercent
//   ApplyDamageToBoss → ReceiveDamage
//   ApplyStatusBoss/ApplyBuffBoss/... → ApplyStatus/ApplyBuff/...
// ══════════════════════════════════════════════════════════════════
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(StatManager))]
[RequireComponent(typeof(StateManager))]
public class BossController : MonoBehaviour, ICombatant
{
    [Header("보스 스탯 에셋")]
    public BossStatsSO bossStats;

    [Header("스탯 매니저 참조")]
    [SerializeField] private StatManager _statManager;

    [Header("상태 매니저 참조")]
    [SerializeField] private StateManager _stateManager;

    [Header("게임 매니저 참조")]
    [SerializeField] private GameManager _gameManager;

    [Header("이펙트 프리팹")]
    public GameObject effectPrefab;

    [Header("ML-Agent 학습")]
    [SerializeField] private bool _trainingMode = false;

    private int          currentPhase = 0;
    private NavMeshAgent agent;
    private bool         isAttacking  = false;

    // ── 외부 접근 ────────────────────────────────────────────
    public StatManager  StatMgr  => _statManager;
    public StateManager StateMgr => _stateManager;
    public int          CurrentPhase => currentPhase;

    public bool TrainingMode
    {
        get => _trainingMode;
        set
        {
            _trainingMode = value;
            ApplyTrainingModeToAgent();
            if (_trainingMode)
            {
                _statManager?.SetCasting(false);
                _statManager?.EndParryWindow();
                _stateManager?.ForceReset();
            }
        }
    }

    // ── ICombatant 프로퍼티 ──────────────────────────────────
    Transform  ICombatant.Transform   => transform;
    GameObject ICombatant.GameObject  => gameObject;
    public float MaxHP            => _statManager.GetMaxHP();
    public float CurrentHPPercent => _statManager.GetHPPercent();
    public float Shield           => _statManager.GetShield();   // 보스는 0
    public bool  IsAlive          => _statManager.IsAlive;
    public bool  IsCasting        => _statManager.IsCasting;
    public bool  IsParrying       => _statManager.IsParrying;    // 보스는 항상 false
    public float ParryWindow      => _statManager.GetParryWindow();

    // ══════════════════════════════════════════════════════════
    // Unity 생명주기
    // ══════════════════════════════════════════════════════════

    void Awake()
    {
        if (_statManager  == null) _statManager  = GetComponent<StatManager>();
        if (_stateManager == null) _stateManager = GetComponent<StateManager>();
        if (_gameManager  == null) _gameManager  = GameManager.Instance;
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.angularSpeed     = 120f;
        agent.stoppingDistance = 2f;

        if (_statManager != null && bossStats != null)
        {
            _statManager.Initialize(
                baseStats:   bossStats,
                maxHP:       bossStats.BossMaxHP,
                shieldMax:   0f,
                hpRegenRate: 0f,
                parryWindow: 0f,
                kind:        CombatantKind.Boss);
            _statManager.BindOwner(this);
        }

        if (_stateManager != null)
            _stateManager.BindOwner(this);

        agent.speed = bossStats.MoveControlMultiplier * 3f;

        if (_trainingMode)
            ApplyTrainingModeToAgent();
    }

    private void ApplyTrainingModeToAgent()
    {
        if (agent == null) return;
        if (_trainingMode)
        {
            agent.enabled = false;
        }
        else
        {
            agent.enabled        = true;
            agent.updatePosition = true;
            agent.updateRotation = true;
            agent.isStopped      = false;
        }
    }

    void Update()
    {
        if (_statManager != null) _statManager.Tick(Time.deltaTime);
        HandlePhase();

        if (!_trainingMode)
        {
            HandleActions();

            if (_stateManager != null)
            {
                bool isMoving = agent != null && agent.velocity.sqrMagnitude > 0.01f;
                _stateManager.NotifyMovementInput(isMoving);
                agent.isStopped = !_stateManager.CanMove;
                _stateManager.Tick(Time.deltaTime);
            }
        }
        else
        {
            if (_stateManager != null)
                _stateManager.Tick(Time.deltaTime);
        }
    }

    // ══════════════════════════════════════════════════════════
    // AI 로직
    // ══════════════════════════════════════════════════════════

    private void HandlePhase()
    {
        if (_statManager == null || bossStats.BossPhaseThresholds == null) return;
        float hpRatio = _statManager.GetHPPercent();

        for (int i = 0; i < bossStats.BossPhaseThresholds.Length; i++)
        {
            if (hpRatio <= bossStats.BossPhaseThresholds[i] && currentPhase < i + 1)
            {
                currentPhase = i + 1;
                OnPhaseChanged(currentPhase);
            }
        }
    }

    public void ResetPhaseForTraining()
    {
        currentPhase = 0;
    }

    private void OnPhaseChanged(int phase)
    {
        Debug.Log("보스 페이즈 전환! 현재 페이즈: " + phase);
        if (!_trainingMode && agent != null)
            agent.speed += 1f;
    }

    private void HandleActions()
    {
        if (isAttacking) return;

        Transform nearest = FindNearestPlayer();
        if (nearest == null) return;

        float distance = Vector3.Distance(transform.position, nearest.position);
        if (distance <= agent.stoppingDistance + 0.5f)
            StartCoroutine(EffectAttackRoutine());
    }

    public Transform FindNearestPlayer()
    {
        if (_gameManager == null) return null;

        Transform best     = null;
        float     bestDist = float.MaxValue;

        foreach (var p in _gameManager.Players)
        {
            if (p == null) continue;
            var combatant = p.GetComponent<ICombatant>();
            if (combatant == null || !combatant.IsAlive) continue;

            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < bestDist) { bestDist = d; best = p.transform; }
        }
        return best;
    }

    private IEnumerator EffectAttackRoutine()
    {
        isAttacking = true;
        if (_stateManager != null) _stateManager.NotifyCastStart();
        else                       _statManager.SetCasting(true);

        float currentRotation = 0f;
        int   spawnCount      = 36;
        float spawnInterval   = 0.2f;
        float effectLifetime  = 0.5f;
        float rotationStep    = 10f;
        float restTime        = 10f;

        for (int i = 0; i < spawnCount; i++)
        {
            Quaternion rotation = Quaternion.Euler(0, currentRotation, 0);
            GameObject fx = Instantiate(effectPrefab, transform.position, rotation);
            Destroy(fx, effectLifetime);
            currentRotation += rotationStep;
            yield return new WaitForSeconds(spawnInterval);
        }

        if (_stateManager != null) _stateManager.NotifyCastEnd();
        else                       _statManager.SetCasting(false);

        yield return new WaitForSeconds(restTime);
        isAttacking = false;
    }

    // ══════════════════════════════════════════════════════════
    // ICombatant — 피해 / 회복 / 실드 (StatManager 로 위임)
    // ══════════════════════════════════════════════════════════

    public void TakeDamage(float amount, ICombatant attacker = null)
    {
        _statManager.ReceiveDamage(amount, attacker);
        if (!IsAlive) Die();
    }

    public void TakeShieldBreakDamage(float amount, float multiplier, ICombatant attacker = null)
    {
        _statManager.ReceiveShieldBreakDamage(amount, multiplier, attacker);
        if (!IsAlive) Die();
    }

    public void RecoverHP(float amount) => _statManager.RecoverHP(amount);
    public void AddShield(float amount) => _statManager.AddShield(amount);   // shieldMax=0 이라 무효

    // ══════════════════════════════════════════════════════════
    // ICombatant — 상태이상 / 버프 / 디버프
    // ══════════════════════════════════════════════════════════

    public void ApplyStatus(StatusType type, float duration, float value = 0f)
        => _statManager.ApplyStatus(type, duration, value);

    public bool HasStatus(StatusType type) => _statManager.HasStatus(type);

    public void ApplyBuff(BuffType type, float duration, float value)
        => _statManager.ApplyBuff(type, duration, value);

    public void ApplyDebuff(DebuffType type, float duration, float value)
        => _statManager.ApplyDebuff(type, duration, value);

    public void RemoveStatuses(CleanseType type, int count)
        => _statManager.RemoveStatuses(type, count);

    public void RemoveBuffs(DispelType type, int count)
        => _statManager.RemoveBuffs(type, count);

    // ══════════════════════════════════════════════════════════
    // ICombatant — 위치 제어 (NavMesh Warp 기반)
    // ══════════════════════════════════════════════════════════

    public void Knockback(Vector3 direction, float distance)
    {
        Vector3 destination = transform.position + direction.normalized * distance;
        agent.Warp(destination);
    }

    public void Pull(Vector3 towardPosition, float distance, float duration)
    {
        Vector3 destination = transform.position + (towardPosition - transform.position).normalized * distance;
        agent.Warp(destination);
    }

    public void MoveBy(Vector3 direction, float distance, float duration, MoveType moveType)
    {
        Vector3 destination = transform.position + direction.normalized * distance;
        agent.Warp(destination);
    }

    // ══════════════════════════════════════════════════════════
    // ICombatant — 패링 보상 (보스도 StatManager 경유)
    // 보스는 패링 스킬이 없지만 인터페이스 통일을 위해 위임
    // ══════════════════════════════════════════════════════════

    public void NotifyParryReward(ParryRewardType rewardType, float value, float duration, ICombatant attacker = null)
        => _statManager.NotifyParryReward(rewardType, value, duration, attacker);

    // ══════════════════════════════════════════════════════════
    // 사망
    // ══════════════════════════════════════════════════════════

    private void Die()
    {
        Debug.Log("보스 처치됨!");
    }
}
