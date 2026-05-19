using UnityEngine;

[CreateAssetMenu(fileName = "BossStatsSO", menuName = "Scriptable Objects/BossStatsSO")]
public class BossStatsSO : BaseStatsSO

{
    // ���� ���� ����
    public float BossMaxHP = 6000f;
    public float BossCurrentHP = 6000f;
    public float BossBaseDamage = 50f;
    public float BossBaseDefense = 20f;

    [Tooltip("������ ��ȯ ���� ü�� ���� (��: 0.75, 0.5, 0.25)")]
    public float[] BossPhaseThresholds;

    public float BossTelegraphTimeMultiplier = 1f;
    public float BossAggroSensitivity = 1f;

}
