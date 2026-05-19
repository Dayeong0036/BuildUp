using UnityEditor;
using UnityEngine;

public static class WireBossInferenceAgent
{
    [MenuItem("Tools/WireBossAgent")]
    public static void Wire()
    {
        var boss = GameObject.Find("(Train) Boss");
        if (boss == null) { Debug.LogError("[Wire] (Train) Boss not found"); return; }

        var p1 = GameObject.Find("Player 1P");
        var p2 = GameObject.Find("Player 2P");

        var agent = boss.GetComponent<BossInferenceAgent>();
        if (agent == null) { Debug.LogError("[Wire] BossInferenceAgent missing"); return; }

        Undo.RecordObject(agent, "Wire BossInferenceAgent");

        var so = new SerializedObject(agent);
        so.Update();

        int wired = 0;
        wired += SetRef(so, "_bossController", boss.GetComponent<BossController>());
        wired += SetRef(so, "_collector", boss.GetComponent<BossObservationCollector>());
        wired += SetRef(so, "_trainingSkillManager", boss.GetComponent<TrainingSkillManager>());
        wired += SetRef(so, "_skillManager", boss.GetComponent<SkillManager>());
        wired += SetRef(so, "_skillExecutor", boss.GetComponent<SkillExecutor>());
        wired += SetRef(so, "_autoCastHelper", boss.GetComponent<BossAutoCastHelper>());
        wired += SetRef(so, "_biasTracker", boss.GetComponent<PlayerBiasTracker>());

        if (p1 != null) wired += SetRef(so, "_p1Object", p1);
        if (p2 != null) wired += SetRef(so, "_p2Object", p2);
        if (p1 != null) wired += SetRef(so, "_p1TrainingSkillMgr", p1.GetComponent<TrainingSkillManager>());
        if (p2 != null) wired += SetRef(so, "_p2TrainingSkillMgr", p2.GetComponent<TrainingSkillManager>());

        var bossSpawn = GameObject.Find("(Train) BossSpawnPoint");
        if (bossSpawn != null) wired += SetRef(so, "_bossSpawnPoint", bossSpawn.transform);

        var spawnProp = so.FindProperty("_playerSpawnPoints");
        if (spawnProp != null)
        {
            string[] names = { "(Train) SpawnPoint1", "(Train) SpawnPoint2", "(Train) SpawnPoint3", "(Train) SpawnPoint4" };
            spawnProp.ClearArray();
            for (int i = 0; i < names.Length; i++)
            {
                var sp = GameObject.Find(names[i]);
                if (sp != null)
                {
                    spawnProp.InsertArrayElementAtIndex(i);
                    spawnProp.GetArrayElementAtIndex(i).objectReferenceValue = sp.transform;
                    wired++;
                }
            }
        }
        else Debug.LogWarning("[Wire] _playerSpawnPoints property not found");

        var bossPoolProp = so.FindProperty("_bossSkillPools");
        if (bossPoolProp != null)
        {
            string[] bossPoolNames = { "BossMeleeAggro", "BossRangedZoner", "BossTankSustain" };
            bossPoolProp.ClearArray();
            for (int i = 0; i < bossPoolNames.Length; i++)
            {
                var guids = AssetDatabase.FindAssets(bossPoolNames[i] + " t:ScriptableObject");
                if (guids.Length > 0)
                {
                    var pool = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    bossPoolProp.InsertArrayElementAtIndex(i);
                    bossPoolProp.GetArrayElementAtIndex(i).objectReferenceValue = pool;
                    wired++;
                    Debug.Log($"[Wire] BossPool[{i}] = {pool.name}");
                }
            }
        }

        var playerPoolProp = so.FindProperty("_playerSkillPools");
        if (playerPoolProp != null)
        {
            var pGuids = AssetDatabase.FindAssets("PlayerTrainingSkillPool t:ScriptableObject");
            playerPoolProp.ClearArray();
            if (pGuids.Length > 0)
            {
                var pool = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(pGuids[0]));
                playerPoolProp.InsertArrayElementAtIndex(0);
                playerPoolProp.GetArrayElementAtIndex(0).objectReferenceValue = pool;
                wired++;
                Debug.Log($"[Wire] PlayerPool = {pool.name}");
            }
        }

        bool applied = so.ApplyModifiedProperties();
        Debug.Log($"[Wire] BossInferenceAgent: {wired} refs set, ApplyModifiedProperties={applied}");

        EditorUtility.SetDirty(agent);
        PrefabUtility.RecordPrefabInstancePropertyModifications(agent);

        // BehaviorParameters: obs=129, branches=[5]
        var bp = boss.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        if (bp != null)
        {
            var bpso = new SerializedObject(bp);
            bpso.Update();
            var brain = bpso.FindProperty("m_BrainParameters");
            if (brain != null)
            {
                brain.FindPropertyRelative("VectorObservationSize").intValue = 129;
                brain.FindPropertyRelative("NumStackedVectorObservations").intValue = 1;
                var actionSpec = brain.FindPropertyRelative("m_ActionSpec");
                if (actionSpec != null)
                    actionSpec.FindPropertyRelative("m_NumContinuousActions").intValue = 0;
            }
            bool bpApplied = bpso.ApplyModifiedProperties();
            EditorUtility.SetDirty(bp);
            PrefabUtility.RecordPrefabInstancePropertyModifications(bp);
            Debug.Log($"[Wire] BehaviorParameters: obs=129, Apply={bpApplied}");
        }

        // BossAutoCastHelper
        var helper = boss.GetComponent<BossAutoCastHelper>();
        if (helper != null)
        {
            Undo.RecordObject(helper, "Wire BossAutoCastHelper");
            var hso = new SerializedObject(helper);
            hso.Update();
            int hw = 0;
            hw += SetRef(hso, "_skillManager", boss.GetComponent<SkillManager>());
            hw += SetRef(hso, "_executor", boss.GetComponent<SkillExecutor>());
            hw += SetRef(hso, "_boss", boss.GetComponent<BossController>());
            hw += SetRef(hso, "_statManager", boss.GetComponent<StatManager>());
            hw += SetRef(hso, "_stateManager", boss.GetComponent<StateManager>());
            bool hApplied = hso.ApplyModifiedProperties();
            Debug.Log($"[Wire] BossAutoCastHelper: {hw} refs set, Apply={hApplied}");
            EditorUtility.SetDirty(helper);
            PrefabUtility.RecordPrefabInstancePropertyModifications(helper);
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Wire] Done + Scene saved");
    }

    static int SetRef(SerializedObject so, string propName, Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop == null) { Debug.LogWarning($"[Wire] Property '{propName}' not found"); return 0; }
        if (value == null) { Debug.LogWarning($"[Wire] Value for '{propName}' is null"); return 0; }
        prop.objectReferenceValue = value;
        Debug.Log($"[Wire] {propName} = {value.name} ({value.GetType().Name})");
        return 1;
    }
}
