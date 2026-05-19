using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ══════════════════════════════════════════════════════════════════
// StatManager — Player / Boss 공용 스탯 매니저 (Stage 2)
//
// 한 개의 전투 개체(플레이어 1명 또는 보스 1마리)가 소유하는 컴포넌트.
// 모든 스탯/피해/상태/패링을 단일 진실 공급원으로 관리한다.
//
// 초기화 시 BaseStatsSO (PlayerStatsSO 또는 BossStatsSO) 를 Instantiate 해서
// 런타임 복사본으로 보존하고, HP/Shield 는 이 클래스 내부 필드로 관리한다.
//
// Stage 2: Deal/Receive Damage, Parry Window, NotifyParryReward (Counter/HitStun)
// Stage 3: ApplyStatus / ApplyBuff / ApplyDebuff / Remove / RecoverHP / AddShield
//          + Parry Invulnerable/Buff 보상 완성
// Stage 4~5: PlayerController / BossController 연결 + 모놀리스 StatsManager 제거 완료
// ══════════════════════════════════════════════════════════════════

public enum CombatantKind
{
    Player,
    Boss,
}

public class StatManager : MonoBehaviour
{
    // ── 런타임 SO 복사본 (Multiplier 전용) ───────────────────────
    private BaseStatsSO _runtimeStats;
    private BaseStatsSO _baseStats;

    // ── 런타임 HP / Shield ───────────────────────────────────────
    private float _maxHP;
    private float _currentHP;
    private float _shieldMax;
    private float _currentShield;
    private float _hpRegenRate;
    private float _parryWindow;
    private float _baseParryWindow;

    private static readonly WaitForSeconds WaitOneSec = new(1f);

    // ── 상태 플래그 ──────────────────────────────────────────────
    private bool _isAlive    = true;
    private bool _isCasting  = false;
    private bool _isParrying = false;

    [Header("디버그")]
    [SerializeField] private bool _logCombat = true;

    // ── Inspector 실시간 모니터 ──────────────────────────────────
    [Header("HP / Shield (실시간)")]
    [SerializeField] private float _debugHP;
    [SerializeField] private float _debugMaxHP;
    [SerializeField] private float _debugShield;
    [SerializeField] private float _debugHPPercent;

    [Header("상태 플래그 (실시간)")]
    [SerializeField] private bool _debugIsAlive;
    [SerializeField] private bool _debugIsCasting;
    [SerializeField] private bool _debugIsParrying;

    [Header("배율 (실시간)")]
    [SerializeField] private float _debugDamageTaken;
    [SerializeField] private float _debugDamageUp;
    [SerializeField] private float _debugMoveControl;
    [SerializeField] private float _debugHealingMult;
    [SerializeField] private float _debugReflectRatio;

    [Header("활성 상태이상 / 버프 / 디버프")]
    [SerializeField] private string[] _debugActiveStatuses = System.Array.Empty<string>();
    [SerializeField] private string[] _debugActiveBuffs    = System.Array.Empty<string>();
    [SerializeField] private string[] _debugActiveDebuffs  = System.Array.Empty<string>();

    // ── 개체 종류 / 소유자 ───────────────────────────────────────
    private CombatantKind _kind;
    private ICombatant    _owner;

    // ── 코루틴 추적 ──────────────────────────────────────────────
    private readonly Dictionary<StatusType, Coroutine> _statusCoroutines = new();
    private readonly Dictionary<BuffType,   Coroutine> _buffCoroutines   = new();
    private readonly Dictionary<DebuffType, Coroutine> _debuffCoroutines = new();
    private Coroutine       _parryCoroutine;
    private WaitForSeconds  _parryWait;

    // ══════════════════════════════════════════════════════════
    // 초기화
    // ══════════════════════════════════════════════════════════

    public void Initialize(
        BaseStatsSO   baseStats,
        float         maxHP,
        float         shieldMax,
        float         hpRegenRate,
        float         parryWindow,
        CombatantKind kind)
    {
        _baseStats       = baseStats;
        _runtimeStats    = baseStats != null ? Instantiate(baseStats) : null;
        _maxHP           = maxHP;
        _currentHP       = maxHP;
        _shieldMax       = shieldMax;
        _currentShield   = 0f;
        _hpRegenRate     = hpRegenRate;
        _parryWindow     = parryWindow;
        _baseParryWindow = parryWindow;
        _kind            = kind;
        _isAlive       = true;
        _isCasting     = false;
        _isParrying    = false;
        _parryWait     = parryWindow > 0f ? new WaitForSeconds(parryWindow) : null;
    }

    // Controller 가 자신을 ICombatant 로 전달해서 attacker 참조용으로 바인딩
    public void BindOwner(ICombatant owner) => _owner = owner;

    // ══════════════════════════════════════════════════════════
    // 조회 — 기본 스탯
    // ══════════════════════════════════════════════════════════

    public float GetHP()          => _currentHP;
    public float GetMaxHP()       => _maxHP;
    public float GetHPPercent()   => _maxHP > 0f ? _currentHP / _maxHP : 0f;
    public float GetShield()      => _currentShield;
    public float GetShieldMax()   => _shieldMax;
    public float GetHPRegenRate() => _hpRegenRate;
    public float GetParryWindow() => _parryWindow;

    public float GetMoveControl()           => _runtimeStats != null ? _runtimeStats.MoveControlMultiplier      : 1f;
    public float GetReflectRatio()          => _runtimeStats != null ? _runtimeStats.ReflectRatio               : 0f;
    public float GetDamageTakenMultiplier() => _runtimeStats != null ? _runtimeStats.DamageTakenMultiplier      : 1f;
    public float GetHealingMultiplier()     => _runtimeStats != null ? _runtimeStats.HealingReceivedMultiplier  : 1f;
    public float GetDamageUpMultiplier()    => _runtimeStats != null ? _runtimeStats.DamageUpMultiplier         : 1f;

    public CombatantKind Kind  => _kind;
    public ICombatant    Owner => _owner;

    // ══════════════════════════════════════════════════════════
    // 상태 플래그
    // ══════════════════════════════════════════════════════════

    public bool IsAlive    => _isAlive;
    public bool IsCasting  => _isCasting;
    public bool IsParrying => _isParrying;

    public void SetCasting(bool value)  => _isCasting  = value;
    public void SetParrying(bool value) => _isParrying = value;

    // ══════════════════════════════════════════════════════════
    // 매 프레임 틱 — Controller 의 Update 에서 호출
    // ══════════════════════════════════════════════════════════
    public void Tick(float dt)
    {
        if (!_isAlive) return;

        if (_hpRegenRate > 0f && _currentHP < _maxHP)
            _currentHP = Mathf.Min(_maxHP, _currentHP + _hpRegenRate * dt);

        RefreshDebugInspector();
    }

    private void RefreshDebugInspector()
    {
        _debugHP         = _currentHP;
        _debugMaxHP      = _maxHP;
        _debugShield     = _currentShield;
        _debugHPPercent  = _maxHP > 0f ? _currentHP / _maxHP : 0f;

        _debugIsAlive    = _isAlive;
        _debugIsCasting  = _isCasting;
        _debugIsParrying = _isParrying;

        _debugDamageTaken  = GetDamageTakenMultiplier();
        _debugDamageUp     = GetDamageUpMultiplier();
        _debugMoveControl  = GetMoveControl();
        _debugHealingMult  = GetHealingMultiplier();
        _debugReflectRatio = GetReflectRatio();

        var statuses = new System.Collections.Generic.List<string>();
        foreach (var kv in _statusCoroutines) statuses.Add(kv.Key.ToString());
        _debugActiveStatuses = statuses.ToArray();

        var buffs = new System.Collections.Generic.List<string>();
        foreach (var kv in _buffCoroutines) buffs.Add(kv.Key.ToString());
        _debugActiveBuffs = buffs.ToArray();

        var debuffs = new System.Collections.Generic.List<string>();
        foreach (var kv in _debuffCoroutines) debuffs.Add(kv.Key.ToString());
        _debugActiveDebuffs = debuffs.ToArray();
    }

    // ══════════════════════════════════════════════════════════
    // 공격 — 공격자 측 (this 의 DamageUp 반영 후 대상 호출)
    // ══════════════════════════════════════════════════════════

    // ── 페이즈 데미지 배율 (버프 DamageUpMultiplier와 독립) ──
    private float _phaseDamageMultiplier = 1f;
    public float PhaseDamageMultiplier => _phaseDamageMultiplier;

    public void SetPhaseDamageMultiplier(float value)
    {
        _phaseDamageMultiplier = Mathf.Max(0f, value);
    }

    public void DealDamage(ICombatant target, float amount)
    {
        if (target == null || !_isAlive) return;
        float adjusted = amount * GetDamageUpMultiplier() * _phaseDamageMultiplier;
        target.TakeDamage(adjusted, _owner);
    }

    public void DealShieldBreakDamage(ICombatant target, float amount, float multiplier)
    {
        if (target == null || !_isAlive) return;
        float adjusted = amount * GetDamageUpMultiplier() * _phaseDamageMultiplier;
        target.TakeShieldBreakDamage(adjusted, multiplier, _owner);
    }

    // ══════════════════════════════════════════════════════════
    // 피해 수신 — ICombatant.TakeDamage 의 본체 (Controller 에서 위임)
    // 처리 순서: 사망체크 → 패링 → 반사 → DamageTakenMultiplier → Shield → HP
    // ══════════════════════════════════════════════════════════

    public void ReceiveDamage(float amount, ICombatant attacker)
    {
        if (!_isAlive) return;

        string myName = _owner?.Transform.name ?? name;
        string atkName = attacker?.Transform.name ?? "?";

        if (_isParrying && attacker != null)
        {
            if (_logCombat) Debug.Log($"[Combat] <b>패링 성공!</b> {myName} → {atkName} 에게 {amount:F0} 반사");
            attacker.TakeDamage(amount, _owner);
            return;
        }

        if (HasStatus(StatusType.Reflecting) && attacker != null)
        {
            float reflected = amount * GetReflectRatio();
            if (_logCombat) Debug.Log($"[Combat] 반사: {myName} → {atkName} 에게 {reflected:F0} 반사 피해");
            attacker.TakeDamage(reflected, _owner);
        }

        float prevHP = _currentHP;
        float prevShield = _currentShield;
        float finalDamage = amount * GetDamageTakenMultiplier();
        ApplyDamageToHPAndShield(finalDamage);

        if (_logCombat)
            Debug.Log($"[Combat] <b>{myName}</b> 피해 수신: {amount:F0} × {GetDamageTakenMultiplier():F2} = {finalDamage:F0}" +
                      $" | HP: {prevHP:F0}→{_currentHP:F0}/{_maxHP:F0} | Shield: {prevShield:F0}→{_currentShield:F0}" +
                      $" | from: {atkName}" +
                      (_currentHP <= 0f ? " | <color=red>사망!</color>" : ""));
    }

    public void ReceiveShieldBreakDamage(float amount, float multiplier, ICombatant attacker)
    {
        if (!_isAlive) return;

        float prevHP = _currentHP;
        float prevShield = _currentShield;
        float shieldDamage = amount * multiplier;
        float overflow     = Mathf.Max(0f, shieldDamage - _currentShield);
        _currentShield     = Mathf.Max(0f, _currentShield - shieldDamage);

        if (overflow > 0f)
            SetHP(_currentHP - overflow * GetDamageTakenMultiplier());

        if (_logCombat)
        {
            string myName = _owner?.Transform.name ?? name;
            Debug.Log($"[Combat] <b>{myName}</b> 실드파괴: {amount:F0}×{multiplier:F1}={shieldDamage:F0}" +
                      $" | Shield: {prevShield:F0}→{_currentShield:F0} | HP: {prevHP:F0}→{_currentHP:F0}" +
                      (_currentHP <= 0f ? " | <color=red>사망!</color>" : ""));
        }
    }

    // ── Shield 우선 소진 → 초과분 HP 로 ──────────────────────────
    private void ApplyDamageToHPAndShield(float finalDamage)
    {
        if (_currentShield > 0f)
        {
            _currentShield -= finalDamage;
            if (_currentShield < 0f)
            {
                SetHP(_currentHP + _currentShield); // _currentShield 는 음수 → HP 에서 차감
                _currentShield = 0f;
            }
        }
        else
        {
            SetHP(_currentHP - finalDamage);
        }
    }

    // ══════════════════════════════════════════════════════════
    // 패링 — Begin/End + 자동 종료 코루틴
    // ══════════════════════════════════════════════════════════

    public void BeginParryWindow()
    {
        if (!_isAlive || _parryWait == null) return;

        if (_parryCoroutine != null)
            StopCoroutine(_parryCoroutine);

        _isParrying      = true;
        _parryCoroutine  = StartCoroutine(ParryWindowRoutine());
    }

    public void EndParryWindow()
    {
        if (_parryCoroutine != null)
        {
            StopCoroutine(_parryCoroutine);
            _parryCoroutine = null;
        }
        _isParrying = false;
    }

    private IEnumerator ParryWindowRoutine()
    {
        yield return _parryWait;
        _isParrying     = false;
        _parryCoroutine = null;
    }

    // ══════════════════════════════════════════════════════════
    // 패링 보상 — 스킬 레이어(ApplyParryReward 부품)가 호출
    // Stage 2 : Counter / HitStun 구현 (ICombatant 경유)
    // Stage 3 : Invulnerable / Buff 본체 구현
    // ══════════════════════════════════════════════════════════
    public void NotifyParryReward(ParryRewardType type, float value, float duration, ICombatant attacker)
    {
        switch (type)
        {
            case ParryRewardType.Counter:
                attacker?.TakeDamage(value, _owner);
                break;

            case ParryRewardType.HitStun:
                attacker?.ApplyStatus(StatusType.HitStun, duration);
                break;

            case ParryRewardType.Invulnerable:
                ApplyStatus(StatusType.Invulnerable, duration, 0f);
                break;

            case ParryRewardType.Buff:
                ApplyBuff(BuffType.DamageUp, duration, value);
                break;
        }
    }

    // ══════════════════════════════════════════════════════════
    // 상태 / 버프 / 디버프 조회
    // ══════════════════════════════════════════════════════════
    public bool HasStatus(StatusType type) => _statusCoroutines.ContainsKey(type);
    public bool HasBuff  (BuffType   type) => _buffCoroutines.ContainsKey(type);
    public bool HasDebuff(DebuffType type) => _debuffCoroutines.ContainsKey(type);

    // ══════════════════════════════════════════════════════════
    // 회복 / 실드
    // ══════════════════════════════════════════════════════════

    public void RecoverHP(float amount)
    {
        if (!_isAlive) return;
        float prev = _currentHP;
        float finalAmount = amount * GetHealingMultiplier();
        SetHP(_currentHP + finalAmount);
        if (_logCombat)
            Debug.Log($"[Combat] <b>{_owner?.Transform.name ?? name}</b> 회복: {finalAmount:F0} | HP: {prev:F0}→{_currentHP:F0}/{_maxHP:F0}");
    }

    public void AddShield(float amount)
    {
        if (!_isAlive) return;
        _currentShield = Mathf.Min(_shieldMax, _currentShield + amount);
    }

    // ══════════════════════════════════════════════════════════
    // 상태이상 — Apply / Remove
    // ══════════════════════════════════════════════════════════

    public void ApplyStatus(StatusType type, float duration, float value = 0f)
    {
        if (!_isAlive || _runtimeStats == null) return;
        if (_logCombat)
            Debug.Log($"[Combat] <b>{_owner?.Transform.name ?? name}</b> 상태부여: {type} ({duration:F1}s, val:{value:F2})");
        float effective = CalcStatusDuration(type, duration);
        if (_statusCoroutines.TryGetValue(type, out var existing) && existing != null)
            StopCoroutine(existing);
        _statusCoroutines[type] = StartCoroutine(StatusRoutine(type, effective, value));
    }

    public void RemoveStatuses(CleanseType type, int count)
    {
        if (_runtimeStats == null) return;
        int removed = 0;
        var toRemove = new List<StatusType>();
        foreach (var kv in _statusCoroutines)
        {
            if (count > 0 && removed >= count) break;
            if (type == CleanseType.All
                || (type == CleanseType.DamageOverTime && kv.Key == StatusType.DamageOverTime)
                || (type == CleanseType.Debuff         && IsDebuffStatus(kv.Key)))
            {
                toRemove.Add(kv.Key);
                if (kv.Value != null) StopCoroutine(kv.Value);
                removed++;
            }
        }
        foreach (var key in toRemove)
        {
            RevertStatus(key);
            _statusCoroutines.Remove(key);
        }
    }

    private float CalcStatusDuration(StatusType type, float duration)
    {
        if (_runtimeStats == null) return duration;
        if (type == StatusType.Stunned || type == StatusType.HitStun)
            return duration * _runtimeStats.StunDurationMultiplier * (1f - _runtimeStats.HitStunResistance);
        if (IsDebuffStatus(type))
            return duration * (1f - _runtimeStats.DebuffDurationResistance);
        return duration;
    }

    private IEnumerator StatusRoutine(StatusType type, float duration, float value)
    {
        ApplyStatusValue(type, value);
        if (type == StatusType.HPRegen || type == StatusType.DamageOverTime)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                yield return WaitOneSec;
                elapsed += 1f;
                if (type == StatusType.HPRegen)        RecoverHP(value);
                if (type == StatusType.DamageOverTime) SetHP(_currentHP - value * GetDamageTakenMultiplier());
            }
        }
        else
        {
            yield return new WaitForSeconds(duration);
        }
        RevertStatus(type);
        _statusCoroutines.Remove(type);
    }

    private void ApplyStatusValue(StatusType type, float value)
    {
        switch (type)
        {
            case StatusType.Stunned:
            case StatusType.HitStun:
            case StatusType.Rooted:
                _runtimeStats.MoveControlMultiplier = 0f; break;
            case StatusType.Slowed:
                _runtimeStats.MoveControlMultiplier *= Mathf.Max(0f, 1f - value); break;
            case StatusType.Vulnerable:
                _runtimeStats.DamageTakenMultiplier += value; break;
            case StatusType.Invulnerable:
                _runtimeStats.DamageTakenMultiplier = 0f; break;
            case StatusType.Reflecting:
                _runtimeStats.ReflectRatio = value; break;
            case StatusType.AntiHeal:
                _runtimeStats.HealingReceivedMultiplier *= Mathf.Max(0f, 1f - value); break;
            case StatusType.Marked:
                _runtimeStats.VulnerabilityBonus += value; break;
            // Silence / HPRegen / DamageOverTime : HasStatus 조회 / 틱 루프로 처리
        }
    }

    private void RevertStatus(StatusType type)
    {
        if (_baseStats == null) return;
        switch (type)
        {
            case StatusType.Stunned:
            case StatusType.HitStun:
            case StatusType.Rooted:
            case StatusType.Slowed:
                _runtimeStats.MoveControlMultiplier = _baseStats.MoveControlMultiplier; break;
            case StatusType.Vulnerable:
            case StatusType.Invulnerable:
                _runtimeStats.DamageTakenMultiplier = _baseStats.DamageTakenMultiplier; break;
            case StatusType.Reflecting:
                _runtimeStats.ReflectRatio = _baseStats.ReflectRatio; break;
            case StatusType.AntiHeal:
                _runtimeStats.HealingReceivedMultiplier = _baseStats.HealingReceivedMultiplier; break;
            case StatusType.Marked:
                _runtimeStats.VulnerabilityBonus = _baseStats.VulnerabilityBonus; break;
        }
    }

    // ══════════════════════════════════════════════════════════
    // 버프 — Apply / Remove
    // ══════════════════════════════════════════════════════════

    public void ApplyBuff(BuffType type, float duration, float value)
    {
        if (!_isAlive || _runtimeStats == null) return;
        if (_buffCoroutines.TryGetValue(type, out var existing) && existing != null)
            StopCoroutine(existing);
        _buffCoroutines[type] = StartCoroutine(BuffRoutine(type, duration, value));
    }

    public void RemoveBuffs(DispelType type, int count)
    {
        if (_runtimeStats == null) return;
        int removed = 0;
        var toRemove = new List<BuffType>();
        foreach (var kv in _buffCoroutines)
        {
            if (count > 0 && removed >= count) break;
            if (type == DispelType.All
                || (type == DispelType.DefenseBuff && IsDefenseBuff(kv.Key))
                || (type == DispelType.OffenseBuff && IsOffenseBuff(kv.Key)))
            {
                toRemove.Add(kv.Key);
                if (kv.Value != null) StopCoroutine(kv.Value);
                removed++;
            }
        }
        foreach (var key in toRemove)
        {
            RevertBuff(key);
            _buffCoroutines.Remove(key);
        }
    }

    private IEnumerator BuffRoutine(BuffType type, float duration, float value)
    {
        ApplyBuffValue(type, value);
        yield return new WaitForSeconds(duration);
        RevertBuff(type);
        _buffCoroutines.Remove(type);
    }

    private void ApplyBuffValue(BuffType type, float value)
    {
        switch (type)
        {
            case BuffType.DamageUp:
                _runtimeStats.DamageUpMultiplier += value / 100f; break;
            case BuffType.DefenseUp:
                _runtimeStats.DefenseUpMultiplier += value / 100f; break;
            case BuffType.ParryWindowUp:
                _parryWindow += _baseParryWindow * (value / 100f);
                _parryWait = _parryWindow > 0f ? new WaitForSeconds(_parryWindow) : null;
                break;
            case BuffType.ParryRewardUp:
                // 별도 필드 없음 — ApplyParryReward 단에서 HasBuff 로 조회
                break;
        }
    }

    private void RevertBuff(BuffType type)
    {
        if (_baseStats == null) return;
        switch (type)
        {
            case BuffType.DamageUp:
                _runtimeStats.DamageUpMultiplier = _baseStats.DamageUpMultiplier; break;
            case BuffType.DefenseUp:
                _runtimeStats.DefenseUpMultiplier = _baseStats.DefenseUpMultiplier; break;
            case BuffType.ParryWindowUp:
                _parryWindow = _baseParryWindow;
                _parryWait = _parryWindow > 0f ? new WaitForSeconds(_parryWindow) : null;
                break;
        }
    }

    // ══════════════════════════════════════════════════════════
    // 디버프 — Apply
    // ══════════════════════════════════════════════════════════

    public void ApplyDebuff(DebuffType type, float duration, float value)
    {
        if (!_isAlive || _runtimeStats == null) return;
        float effective = duration * (1f - _runtimeStats.DebuffDurationResistance);
        if (_debuffCoroutines.TryGetValue(type, out var existing) && existing != null)
            StopCoroutine(existing);
        _debuffCoroutines[type] = StartCoroutine(DebuffRoutine(type, effective, value));
    }

    private IEnumerator DebuffRoutine(DebuffType type, float duration, float value)
    {
        ApplyDebuffValue(type, value);
        yield return new WaitForSeconds(duration);
        RevertDebuff(type);
        _debuffCoroutines.Remove(type);
    }

    private void ApplyDebuffValue(DebuffType type, float value)
    {
        switch (type)
        {
            case DebuffType.DamageDown:
            case DebuffType.SelfDefenseDown:
                _runtimeStats.DamageUpMultiplier = Mathf.Max(0f, _runtimeStats.DamageUpMultiplier - value / 100f); break;
            case DebuffType.DefenseDown:
                _runtimeStats.DamageTakenMultiplier += value / 100f; break;
            case DebuffType.Mark:
                _runtimeStats.VulnerabilityBonus += value; break;
        }
    }

    private void RevertDebuff(DebuffType type)
    {
        if (_baseStats == null) return;
        switch (type)
        {
            case DebuffType.DamageDown:
            case DebuffType.SelfDefenseDown:
                _runtimeStats.DamageUpMultiplier = _baseStats.DamageUpMultiplier; break;
            case DebuffType.DefenseDown:
                _runtimeStats.DamageTakenMultiplier = _baseStats.DamageTakenMultiplier; break;
            case DebuffType.Mark:
                _runtimeStats.VulnerabilityBonus = _baseStats.VulnerabilityBonus; break;
        }
    }

    // ══════════════════════════════════════════════════════════
    // 학습용 전체 리셋 — 에피소드 시작 시 호출
    // ══════════════════════════════════════════════════════════

    public void ResetForTraining()
    {
        foreach (var kv in _statusCoroutines)
            if (kv.Value != null) StopCoroutine(kv.Value);
        _statusCoroutines.Clear();

        foreach (var kv in _buffCoroutines)
            if (kv.Value != null) StopCoroutine(kv.Value);
        _buffCoroutines.Clear();

        foreach (var kv in _debuffCoroutines)
            if (kv.Value != null) StopCoroutine(kv.Value);
        _debuffCoroutines.Clear();

        if (_parryCoroutine != null)
        {
            StopCoroutine(_parryCoroutine);
            _parryCoroutine = null;
        }

        if (_baseStats != null && _runtimeStats != null)
        {
            _runtimeStats.MoveControlMultiplier     = _baseStats.MoveControlMultiplier;
            _runtimeStats.DamageTakenMultiplier      = _baseStats.DamageTakenMultiplier;
            _runtimeStats.ReflectRatio               = _baseStats.ReflectRatio;
            _runtimeStats.HealingReceivedMultiplier  = _baseStats.HealingReceivedMultiplier;
            _runtimeStats.DamageUpMultiplier          = _baseStats.DamageUpMultiplier;
            _runtimeStats.VulnerabilityBonus          = _baseStats.VulnerabilityBonus;
            _phaseDamageMultiplier                     = 1f;

            if (_baseStats is BossStatsSO bossSO)
            {
                _maxHP = bossSO.BossMaxHP;
            }
            else if (_baseStats is PlayerStatsSO playerSO)
            {
                _maxHP     = playerSO.MaxHP;
                _shieldMax = playerSO.ShieldMax;
            }
        }

        _currentHP     = _maxHP;
        _currentShield = 0f;
        _isAlive       = true;
        _isCasting     = false;
        _isParrying    = false;
    }

    // ══════════════════════════════════════════════════════════
    // 내부 유틸리티
    // ══════════════════════════════════════════════════════════

    protected internal void SetHP(float value)
    {
        _currentHP = Mathf.Clamp(value, 0f, _maxHP);
        if (_currentHP <= 0f) _isAlive = false;
    }

    protected internal void SetShield(float value)
    {
        _currentShield = Mathf.Clamp(value, 0f, _shieldMax);
    }

    // ══════════════════════════════════════════════════════════
    // 분류 헬퍼
    // ══════════════════════════════════════════════════════════

    private static bool IsDebuffStatus(StatusType type) =>
        type == StatusType.Slowed     || type == StatusType.Rooted     || type == StatusType.Stunned ||
        type == StatusType.HitStun    || type == StatusType.Vulnerable || type == StatusType.Silence ||
        type == StatusType.AntiHeal   || type == StatusType.Marked     || type == StatusType.DamageOverTime;

    private static bool IsDefenseBuff(BuffType type) =>
        type == BuffType.DefenseUp;

    private static bool IsOffenseBuff(BuffType type) =>
        type == BuffType.DamageUp || type == BuffType.ParryWindowUp || type == BuffType.ParryRewardUp;
}
