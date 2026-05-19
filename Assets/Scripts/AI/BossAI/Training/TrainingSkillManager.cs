using UnityEngine;

// 학습 환경 전용 스킬 해금 관리
// 에피소드 시작 시 풀에서 랜덤 셔플 → 점진 해금 (최대 3개)
public class TrainingSkillManager : MonoBehaviour
{
    private const int MaxSlots = 5;

    [Header("스킬 풀")]
    [SerializeField] private SkillPoolSO _skillPool;

    [Header("해금 설정")]
    [SerializeField] private int   _initialUnlockCount = 1;
    [SerializeField] private int   _maxUnlockCount     = 5;
    [SerializeField] private float _unlockInterval     = 175f;

    [Header("참조")]
    [SerializeField] private SkillManager _skillManager;
    [SerializeField] private BossObservationCollector _collector;
    [SerializeField] private SkillExecutor _executor;

    private int   _unlockedCount;
    private float _nextUnlockTime;

    private int[] _shuffledIndices;
    private readonly SkillDefinition[] _equippedSkills = new SkillDefinition[MaxSlots];

    public int UnlockedCount => _unlockedCount;
    public SkillPoolSO SkillPool => _skillPool;

    public void SetSkillPool(SkillPoolSO pool) => _skillPool = pool;

    public void SetUnlockConfig(int initial, int max)
    {
        _initialUnlockCount = Mathf.Clamp(initial, 0, MaxSlots);
        _maxUnlockCount     = Mathf.Clamp(max, 0, MaxSlots);
    }

    private void Awake()
    {
        if (_skillManager == null) _skillManager = GetComponent<SkillManager>();
        if (_collector == null)    _collector    = GetComponent<BossObservationCollector>();
        if (_executor == null)     _executor     = GetComponent<SkillExecutor>();
    }

    public void ResetForEpisode()
    {
        _skillManager.ClearAll();
        _unlockedCount = 0;

        for (int i = 0; i < MaxSlots; i++)
            _equippedSkills[i] = null;

        ShufflePool();

        int initial = Mathf.Clamp(_initialUnlockCount, 0, MaxSlots);
        for (int i = 0; i < initial; i++)
            UnlockNextSlot();

        _nextUnlockTime = Time.time + _unlockInterval;
        SyncCollector();
    }

    private void ShufflePool()
    {
        if (_skillPool == null || _skillPool.Count == 0)
        {
            _shuffledIndices = null;
            return;
        }

        int count = _skillPool.Count;
        _shuffledIndices = new int[count];
        for (int i = 0; i < count; i++)
            _shuffledIndices[i] = i;

        // Fisher-Yates
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_shuffledIndices[i], _shuffledIndices[j]) = (_shuffledIndices[j], _shuffledIndices[i]);
        }
    }

    public void Tick()
    {
        int cap = Mathf.Clamp(_maxUnlockCount, 0, MaxSlots);
        if (_unlockedCount >= cap) return;
        if (Time.time < _nextUnlockTime) return;

        UnlockNextSlot();
        _nextUnlockTime = Time.time + _unlockInterval;
        SyncCollector();
    }

    private void UnlockNextSlot()
    {
        if (_skillPool == null || _unlockedCount >= MaxSlots) return;
        if (_shuffledIndices == null || _unlockedCount >= _shuffledIndices.Length) return;

        int poolIndex = _shuffledIndices[_unlockedCount];
        SkillDefinition skill = _skillPool.GetSkill(poolIndex);
        if (skill == null) return;

        _equippedSkills[_unlockedCount] = skill;
        _skillManager.SetSlot(_unlockedCount, skill);
        _unlockedCount++;
    }

    private void SyncCollector()
    {
        if (_collector == null) return;

        _collector.SetUnlockedSlotCount(_unlockedCount);
        for (int i = 0; i < MaxSlots; i++)
            _collector.SetBossSkill(i, _equippedSkills[i]);
    }

    public SkillDefinition GetEquippedSkill(int slot)
    {
        if (slot < 0 || slot >= MaxSlots) return null;
        return _equippedSkills[slot];
    }

    public bool IsSlotUnlocked(int slot) => slot >= 0 && slot < _unlockedCount;

    public bool CanUseSlot(int slot)
    {
        if (!IsSlotUnlocked(slot)) return false;

        SkillDefinition skill = _equippedSkills[slot];
        if (skill == null) return false;

        return _executor != null && _executor.CanUse(skill);
    }
}
