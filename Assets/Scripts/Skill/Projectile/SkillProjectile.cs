using UnityEngine;

// 스킬 투사체 프리팹에 붙는 컴포넌트
// ProjectilePool에서 꺼내서 Launch → 위치 기반 OverlapSphere 판정 → onHit(SkillStep) 실행 → 풀 반환
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class SkillProjectile : MonoBehaviour, IProjectile
{
    [SerializeField] private Color _color = Color.red;
    [SerializeField] private float _detectionRadius = 1.0f;
    [SerializeField] private LayerMask _targetMask = -1;

    private const int OverlapBufferSize = 16;
    private static readonly Collider[] _overlapBuffer = new Collider[OverlapBufferSize];

    private Rigidbody   _rb;
    private Collider    _col;
    private Renderer    _renderer;
    private ProjectilePool _pool;

    private SkillStep   _onHit;
    private SkillContext _ctx;
    private bool        _pierce;
    private float       _range;
    private Vector3     _spawnPos;
    private bool        _active;
    private readonly System.Collections.Generic.HashSet<ICombatant> _hitTargets = new();

    private void Awake()
    {
        _rb           = GetComponent<Rigidbody>();
        _col          = GetComponent<Collider>();
        _renderer     = GetComponentInChildren<Renderer>();
        _rb.useGravity = false;
        _col.isTrigger = true;
    }

    public void SetPool(ProjectilePool pool) => _pool = pool;

    // ── IProjectile ──────────────────────────────────────────

    public void Launch(Vector3 direction, float speed, float range)
    {
        _range    = range;
        _spawnPos = transform.position;
        _active   = true;
        _rb.linearVelocity = direction.normalized * speed;
    }

    public void SetHitCallback(SkillStep onHit, SkillContext ctx, bool pierce)
    {
        _onHit  = onHit;
        _ctx    = ctx;
        _pierce = pierce;
    }

    // ── 위치 기반 판정 (FixedUpdate — 물리 틱과 동기화) ──────
    private void FixedUpdate()
    {
        if (!_active) return;

        if (Vector3.Distance(_spawnPos, transform.position) >= _range)
        {
            ReturnToPool();
            return;
        }

        if (!ShouldRunHitDetection()) return;

        int count = Physics.OverlapSphereNonAlloc(transform.position, _detectionRadius, _overlapBuffer, _targetMask);

        for (int i = 0; i < count; i++)
        {
            var target = _overlapBuffer[i].GetComponentInParent<ICombatant>();
            if (target == null) continue;
            if (_ctx != null && ReferenceEquals(target, _ctx.Caster)) continue;
            if (!_hitTargets.Add(target)) continue;

            ApplyHit(target);

            if (!_pierce) { ReturnToPool(); return; }
        }
    }

    // ── 권위 가드 — 서버 전환 시 이 메서드만 교체 ────────────
    private bool ShouldRunHitDetection()
    {
        // 현재: 클라이언트 로직이므로 항상 실행
        // 서버 전환 시: NetworkManager.IsServer / IsHost 체크로 교체
        return true;
    }

    // ── 적중 처리 — 서버 전환 시 RPC 발송 지점 ───────────────
    private void ApplyHit(ICombatant target)
    {
        if (_ctx != null)
        {
            _ctx.PrimaryTarget = target;
            _ctx.HitLanded     = true;
            _ctx.CastPosition  = transform.position;
            _ctx.RefreshSnapshot();
            _ctx.OnHitRecorded?.Invoke();
        }

        _onHit?.Invoke(_ctx);
    }

    // ── IPoolable ────────────────────────────────────────────

    public void OnSpawn()
    {
        _active            = false;
        _onHit             = null;
        _ctx               = null;
        _pierce            = false;
        _rb.linearVelocity = Vector3.zero;
        _hitTargets.Clear();
        if (_renderer != null) _renderer.material.color = _color;
    }

    public void OnDespawn()
    {
        _active            = false;
        _rb.linearVelocity = Vector3.zero;
        _hitTargets.Clear();
    }

    private void ReturnToPool()
    {
        _active = false;
        _pool?.Return(this);
    }
}
