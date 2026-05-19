using System.Collections.Generic;

// 보스 스킬별 Phase 1 기준 텔레그래프 duration (핸드오프 §8)
// BossAutoCastHelper가 참조, 페이즈 배율은 별도 적용
public static class SkillTelegraphTable
{
    private static readonly Dictionary<string, float> BaseDurations = new()
    {
        { "ExecutionSpike_Boss",   1.35f },
        { "CrushingBarrage_Boss",  1.2f  },
        { "FortressArmor_Boss",    1.3f  },
        { "CollapseRoar_Boss",     1.6f  },
        { "MarkWave_Boss",         1.4f  },
        { "BarrierBreaker_Boss",   1.4f  },
        { "ErosionField_Boss",     1.6f  },
        { "SurvivalPulse_Boss",    0f    },
        { "OverchargeMode_Boss",   0f    },
    };

    public static float GetBaseDuration(string skillId)
        => BaseDurations.TryGetValue(skillId, out float d) ? d : 0f;
}
