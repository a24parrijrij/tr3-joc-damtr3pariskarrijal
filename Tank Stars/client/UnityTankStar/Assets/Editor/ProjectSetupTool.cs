using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.IO;

public class ProjectSetupTool : EditorWindow
{
    [MenuItem("Tools/Setup Project")]
    public static void RunSetup()
    {
        // 🔄 REFRESH TRIGGER: Forces Unity to sync the folder after the cleanup.
        if (Application.isPlaying)
        {
            Debug.LogError("Please stop Play Mode before running Project Setup!");
            return;
        }

        // 🛠️ MODERN INPUT FIX: Set to 'Both' (2) to support Legacy + New
        SerializedObject settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var prop = settings.FindProperty("activeInputHandler");
        if (prop != null)
        {
            prop.intValue = 2;
            settings.ApplyModifiedProperties();
        }

        CreateScenes();
        SetupBuildSettings();
        SetupCombatScene();
        SetupVsAIScene();
        SetupTrainingScene(); // New: Requirements fulfillment
        SetupTags();
        DeepProjectClean(); // 🧹 FINAL CLEANUP: Safely un-syncs the obsolete files
        Debug.Log("Master Project Setup Complete! Open LoginScene and press Play.");
    }

    private static void DeepProjectClean()
    {
        string[] obsolete = {
            "Assets/Editor/VsAISetupTool.cs",
            "Assets/Editor/AgentSetupTool.cs",
            "Assets/Editor/GameObjectBuilder.cs",
            "Assets/Editor/PropertyDebugger.cs"
        };

        foreach (var path in obsolete)
        {
            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
                Debug.Log($"Deep Clean: Safely removed obsolete script -> {path}");
            }
        }
        AssetDatabase.Refresh();
    }

    private static void CreateScenes()
    {
        string[] scenes = { "CombatScene", "LoginScene", "MenuScene", "WaitingScene", "TrainingScene", "VsAIScene" };
        foreach (var sceneName in scenes)
        {
            string path = $"Assets/Scenes/{sceneName}.unity";
            if (!File.Exists(path))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, path);
            }
        }

        // Delete SampleScene
        string samplePath = "Assets/Scenes/SampleScene.unity";
        if (File.Exists(samplePath))
        {
            AssetDatabase.DeleteAsset(samplePath);
        }
        AssetDatabase.Refresh();
    }

    private static void SetupBuildSettings()
    {
        string[] scenesToOrder = {
            "Assets/Scenes/LoginScene.unity",
            "Assets/Scenes/MenuScene.unity",
            "Assets/Scenes/WaitingScene.unity",
            "Assets/Scenes/CombatScene.unity",
            "Assets/Scenes/VsAIScene.unity"
        };

        List<EditorBuildSettingsScene> buildScenes = new List<EditorBuildSettingsScene>();
        foreach (var path in scenesToOrder)
        {
            if (File.Exists(path))
            {
                buildScenes.Add(new EditorBuildSettingsScene(path, true));
            }
        }
        EditorBuildSettings.scenes = buildScenes.ToArray();
    }

    private static void SetupCombatScene()
    {
        string path = "Assets/Scenes/CombatScene.unity";
        var scene = EditorSceneManager.OpenScene(path);

        foreach (var rootGo in scene.GetRootGameObjects())
            GameObject.DestroyImmediate(rootGo);

        // Setup Main Camera
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        var cam = camGo.GetComponent<Camera>();
        camGo.transform.position = new Vector3(0, 0, -10);
        cam.backgroundColor = new Color(0.06f, 0.08f, 0.15f);
        cam.orthographic = true;
        cam.orthographicSize = 6;
        camGo.tag = "MainCamera";

        // Manager & UI
        var mgrGo = new GameObject("CombatManager");
        var uiDoc = mgrGo.AddComponent<UIDocument>();
        uiDoc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/UXML/CombatScreen.uxml");
        var panelSettings = LoadPanelSettings();
        if (panelSettings != null) uiDoc.panelSettings = panelSettings;
        mgrGo.AddComponent<CombatManager>();

        EditorSceneManager.SaveScene(scene);
    }

    private static void SetupVsAIScene()
    {
        string path = "Assets/Scenes/VsAIScene.unity";
        var scene = EditorSceneManager.OpenScene(path);

        foreach (var rootGo in scene.GetRootGameObjects())
            GameObject.DestroyImmediate(rootGo);

        // 1. Camera
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        var cam = camGo.GetComponent<Camera>();
        camGo.transform.position = new Vector3(0, 0, -10);
        cam.backgroundColor = new Color(0.1f, 0.12f, 0.2f);
        cam.orthographic = true;
        cam.orthographicSize = 6.5f;
        camGo.tag = "MainCamera";

        // 2. Terrain
        var terrainGo = new GameObject("Terrain");
        var tg = terrainGo.AddComponent<TerrainGenerator>();
        terrainGo.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));

        // 3. Tanks
        GameObject p1 = BuildTank("PlayerHuman", true);
        GameObject p2 = BuildTank("AI_Agent", false);
        
        var tc1 = p1.GetComponent<TankController>();
        var tc2 = p2.GetComponent<TankController>();
        tc1.terrain = tg;
        tc2.terrain = tg;

        // 4. ML Agent Logic
        var agent = p2.AddComponent<TankAgent>();
        agent.localTank = tc2;
        agent.enemyTank = tc1;
        agent.terrain   = tg;
        agent.isVsAIMode = true; // Set here to prevent OnEpisodeBegin race
        agent.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectile.prefab");
        agent.explosionPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Explosion.prefab");

        var bp = p2.AddComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        // 3. Configure AI 
        bp.BehaviorName = "TankBehavior";
        bp.BehaviorType = Unity.MLAgents.Policies.BehaviorType.InferenceOnly; // 🧠 BRAIN IS ON!
        
        var model = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Models/TankBehavior.onnx");
        if (model != null)
        {
            var so = new SerializedObject(bp);
            so.FindProperty("m_Model").objectReferenceValue = model;
            so.ApplyModifiedProperties();
        }
        bp.BehaviorType = Unity.MLAgents.Policies.BehaviorType.InferenceOnly;

        // --- 🧪 AI BRAIN MOTOR ---
        // Restore DecisionRequester so the inference engine actually runs.
        // We use Period 1 but use canCaptureActions in TankAgent to lock it.
        var dr = p2.AddComponent<Unity.MLAgents.DecisionRequester>();
        dr.DecisionPeriod = 1;
        dr.TakeActionsBetweenDecisions = false;
        
        // 5. Human Input
        var hInput = p1.AddComponent<HumanTankInput>();
        hInput.tank = tc1;

        // 6. Manager & UI (CRITICAL FIX: Adding UI components)
        var mgrGo = new GameObject("VsAIManager");
        
        // Add UI Components
        var uiDoc = mgrGo.AddComponent<UIDocument>();
        uiDoc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/UXML/CombatScreen.uxml");
        
        // Link Panel Settings (Required for UI Toolkit)
        var panelSettings = LoadPanelSettings();
        if (panelSettings != null) uiDoc.panelSettings = panelSettings;

        var vsMgr = mgrGo.AddComponent<VsAIManager>();
        vsMgr.terrain = tg;
        vsMgr.playerTank = tc1;
        vsMgr.aiTank = tc2;
        vsMgr.aiAgent = agent;
        hInput.manager = vsMgr;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetupTrainingScene()
    {
        string path = "Assets/Scenes/TrainingScene.unity";
        var scene = EditorSceneManager.OpenScene(path);

        foreach (var rootGo in scene.GetRootGameObjects())
            GameObject.DestroyImmediate(rootGo);

        // 1. Camera & Terrain
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.transform.position = new Vector3(0, 0, -10);
        camGo.GetComponent<Camera>().orthographic = true;
        camGo.GetComponent<Camera>().orthographicSize = 6.5f;

        var terrainGo = new GameObject("Terrain");
        var tg = terrainGo.AddComponent<TerrainGenerator>();
        terrainGo.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));

        // 2. Tanks
        GameObject agentObj = BuildTank("AI_Agent_Trainer", false);
        GameObject targetObj = BuildTank("Training_Target", true);
        
        var tc1 = agentObj.GetComponent<TankController>();
        var tc2 = targetObj.GetComponent<TankController>();
        tc1.terrain = tg;
        tc2.terrain = tg;

        // 3. Agent Config
        var agent = agentObj.AddComponent<TankAgent>();
        agent.localTank = tc1;
        agent.enemyTank = tc2;
        agent.terrain   = tg;
        agent.isVsAIMode = false; // Training mode
        agent.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectile.prefab");
        agent.explosionPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Explosion.prefab");

        var bp = agentObj.AddComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        bp.BehaviorName = "TankBehavior";
        
        // --- 🧠 MODEL-DRIVEN AI CONFIGURATION ---
        // We no longer force VectorObservationSize/ActionSpec here.
        // The .onnx file will automatically configure these in Inference mode.
        bp.BehaviorType = Unity.MLAgents.Policies.BehaviorType.HeuristicOnly; 

        agentObj.AddComponent<Unity.MLAgents.DecisionRequester>().DecisionPeriod = 1;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static GameObject BuildTank(string name, bool isPlayer)
    {
        var tank = new GameObject(name);
        tank.tag = "Tank";
        
        var body = new GameObject("Body");
        body.transform.SetParent(tank.transform);
        var bodySr = body.AddComponent<SpriteRenderer>();
        bodySr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(isPlayer ? "Assets/Sprites/Tanks/tank_blue_body.png" : "Assets/Sprites/Tanks/tank_red_body.png");
        bodySr.sortingOrder = 1;

        var barrel = new GameObject("Barrel");
        barrel.transform.SetParent(tank.transform);
        barrel.transform.localPosition = new Vector3(isPlayer ? 0.3f : -0.3f, 0.2f, 0);
        var barrelSr = barrel.AddComponent<SpriteRenderer>();
        barrelSr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(isPlayer ? "Assets/Sprites/Tanks/tank_blue_barrel.png" : "Assets/Sprites/Tanks/tank_red_barrel.png");
        barrelSr.sortingOrder = 2;

        var tc = tank.AddComponent<TankController>();
        tc.barrel = barrel.transform;

        var rb = tank.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        tank.AddComponent<BoxCollider2D>().size = new Vector2(1.2f, 0.6f);

        return tank;
    }

    private static PanelSettings LoadPanelSettings()
    {
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/New Panel Settings.asset");
        if (panelSettings != null) return panelSettings;

        string[] guids = AssetDatabase.FindAssets("t:PanelSettings");
        if (guids.Length == 0) return null;

        string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath);
    }

    private static void SetupTags()
    {
        var tagAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagAssets.Length == 0) return;

        SerializedObject tagManager = new SerializedObject(tagAssets[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        bool found = false;
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue.Equals("Tank"))
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            tagsProp.InsertArrayElementAtIndex(0);
            tagsProp.GetArrayElementAtIndex(0).stringValue = "Tank";
            tagManager.ApplyModifiedProperties();
        }
    }
}
