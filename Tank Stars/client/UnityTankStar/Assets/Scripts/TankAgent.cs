using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class TankAgent : Agent
{
    [Header("Environment References")]
    public TerrainGenerator terrain;
    public TankController   localTank;
    public TankController   enemyTank;
    public GameObject       projectilePrefab;
    public GameObject       explosionPrefab;

    [Header("State Control")]
    public bool isVsAIMode        = false;
    public bool isWaitingForShot  = false;
    public bool canCaptureActions = false;

    // NOTE: Do NOT override Initialize() to change BrainParameters at runtime.
    // ML-Agents reads BrainParameters from the serialized BehaviorParameters component
    // BEFORE Initialize() is called. Any changes made inside Initialize() are ignored
    // by the time the model is loaded. Set the correct values directly on the
    // BehaviorParameters component in the Inspector:
    //   Vector Observation > Space Size = 5
    //   Actions > Continuous Actions    = 2
    //   Actions > Discrete Branches     = 0

    public override void OnEpisodeBegin()
    {
        isWaitingForShot = false;

        if (!isVsAIMode)
        {
            if (terrain != null)
                terrain.GenerateTerrain(Random.Range(1, 10000), "desert");

            if (localTank != null)
            {
                localTank.transform.position = new Vector3(Random.Range(-8f, -2f), 0, 0);
                localTank.PlaceOnTerrain();
                localTank.currentHp = localTank.maxHp;
            }
            if (enemyTank != null)
            {
                enemyTank.transform.position = new Vector3(Random.Range(2f, 8f), 0, 0);
                enemyTank.PlaceOnTerrain();
                enemyTank.currentHp = enemyTank.maxHp;
            }
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (localTank == null || enemyTank == null)
        {
            for (int i = 0; i < 5; i++) sensor.AddObservation(0f);
            return;
        }

        sensor.AddObservation(localTank.transform.position.x / 10f);
        sensor.AddObservation(enemyTank.transform.position.x / 10f);
        sensor.AddObservation((enemyTank.transform.position.x - localTank.transform.position.x) / 20f);
        sensor.AddObservation(localTank.currentHp / 100f);
        sensor.AddObservation(enemyTank.currentHp / 100f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 📝 LOG: Decision received!
        if (!canCaptureActions) { Debug.Log("[TankAgent] ACTION BLOCKED: Not AI's turn."); return; }
        if (isWaitingForShot) { Debug.Log("[TankAgent] ACTION BLOCKED: Projectile in air."); return; }

        float angle = 45f;
        float power = 75f;

        // Support BOTH Continuous and Discrete models
        if (actions.ContinuousActions.Length >= 2)
        {
            angle = ((actions.ContinuousActions[0] + 1f) / 2f) * 90f;
            power = ((actions.ContinuousActions[1] + 1f) / 2f) * 100f;
            Debug.Log($"[TankAgent] Brain (Continuous) -> Angle: {angle:F1}, Power: {power:F1}");
        }
        else if (actions.DiscreteActions.Length >= 2)
        {
            // Map [0, 90] discrete angle and [0, 100] discrete power
            angle = actions.DiscreteActions[0];
            power = actions.DiscreteActions[1];
            Debug.Log($"[TankAgent] Brain (Discrete) -> Angle: {angle:F1}, Power: {power:F1}");
        }
        else
        {
            Debug.LogError("[TankAgent] ERROR: Model has neither enough Continuous (2) nor Discrete (2) actions!");
            return;
        }

        FireActualShot(angle, power);

        if (!isVsAIMode) AddReward(-0.001f);
    }

    public void FireActualShot(float angle, float power)
    {
        if (projectilePrefab == null || localTank == null)
        {
            Debug.LogError("[TankAgent] FireActualShot: projectilePrefab or localTank is null!");
            return;
        }

        isWaitingForShot = true;

        Vector3 spawnPos = localTank.barrel != null
            ? localTank.barrel.position
            : localTank.transform.position + Vector3.up * 0.5f;

        GameObject proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        Collider2D pCol = proj.GetComponent<Collider2D>();
        Collider2D tCol = localTank.GetComponent<Collider2D>();
        if (pCol != null && tCol != null) Physics2D.IgnoreCollision(pCol, tCol);

        if (enemyTank != null)
            localTank.SetBarrelAngle(angle, localTank.transform.position.x < enemyTank.transform.position.x);

        var pc = proj.GetComponent<ProjectileController>();
        if (pc == null)
        {
            Debug.LogError("[TankAgent] Projectile prefab is missing ProjectileController!");
            Destroy(proj);
            isWaitingForShot = false;
            return;
        }

        pc.SetImpactCallback(OnProjectileImpact);
        pc.Launch(angle, power, enemyTank != null && localTank.transform.position.x < enemyTank.transform.position.x);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Aims roughly at the enemy using a simple ballistic estimate
        var actions = actionsOut.ContinuousActions;
        if (actions.Length < 2 || localTank == null || enemyTank == null)
        {
            if (actions.Length >= 2) { actions[0] = 0f; actions[1] = 0f; }
            return;
        }

        float dist = Mathf.Abs(enemyTank.transform.position.x - localTank.transform.position.x);
        // Simple heuristic: angle ~45° scales with distance, power scales with distance
        float normAngle = Mathf.Clamp(45f / 90f, 0f, 1f);       // ~45° → maps to 0 in [-1,1]
        float normPower = Mathf.Clamp(dist / 18f, 0.1f, 1f);    // distance-based power
        // Convert [0,1] → [-1,1] for the continuous action space
        actions[0] = normAngle * 2f - 1f;
        actions[1] = normPower * 2f - 1f;
    }

    private void OnProjectileImpact(Vector2 impactWorld, bool hitTank)
    {
        if (isVsAIMode)
        {
            if (hitTank && enemyTank != null)
            {
                enemyTank.TakeDamage(35);
            }
            else if (enemyTank != null)
            {
                float d = Vector2.Distance(impactWorld, enemyTank.transform.position);
                if (d < 1.5f) enemyTank.TakeDamage(15);
            }

            if (terrain != null)
            {
                terrain.DestroyTerrain(impactWorld, 0.8f);
                localTank?.PlaceOnTerrain();
                enemyTank?.PlaceOnTerrain();
            }

            if (explosionPrefab != null)
            {
                GameObject exp = Instantiate(explosionPrefab,
                    new Vector3(impactWorld.x, impactWorld.y, 0f), Quaternion.identity);
                Destroy(exp, 2f);
            }

            isWaitingForShot = false;
            VsAIManager.Instance?.OnProjectileResolved();
        }
        else
        {
            // Training rewards
            if (hitTank)
            {
                AddReward(1.0f);
                EndEpisode();
            }
            else
            {
                float d = enemyTank != null
                    ? Vector2.Distance(impactWorld, enemyTank.transform.position)
                    : 10f;
                AddReward(-0.01f * d);
                if (d > 5f) EndEpisode();
            }
            isWaitingForShot = false;
        }
    }
}