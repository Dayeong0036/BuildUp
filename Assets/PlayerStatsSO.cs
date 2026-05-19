using UnityEngine;

[CreateAssetMenu(fileName = "PlayerStatsSO", menuName = "Scriptable Objects/PlayerStatsSO")]
public class PlayerStatsSO : BaseStatsSO

{
    // �⺻ ���� ����
    public float MaxHP = 150f;
    public float CurrentHP = 150f;
    public float BaseDamage = 10f;
    public float BaseDefense = 5f;

    // �̵� / �⵿ ����
    public float MoveSpeed = 14f;
    public float TurnSpeed = 180f;
    public float ActionSpeed = 1f;
    public float MoveAcceleration = 10f;
    public float MoveDeceleration = 10f;

    // ���� �׼� ����
    public float RopeRange = 10f;
    public float RopeSpeed = 15f;
    public float RopeCooldown = 5f;
    public float RopeAttachTime = 0.2f;
    public float RopeReleaseRecovery = 0.5f;

    // ���� / ��ų ����
    public float AttackAreaScale = 1f;
    public float SkillPower = 1f;

    // �и� / ���� ����
    public float ParryWindow = 0.3f;
    public float ParryCooldown = 2f;
    public float CounterWindow = 0.5f;

    // ���� / ȸ�� ����
    public float ShieldMax = 50f;
    public float CurrentShield = 0f;
    public float HPRegenRate = 1f;
    public float ReviveTime = 5f;

    // ���� / ������ ���� ����
    public float AggroWeight = 1f;

}
