using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class TankAgent : Agent
{
    [Header("Environment References")]
    public TerrainGenerator terrain;
    public TankController localTank;
    public TankController enemyTank;
    public GameObject projectilePrefab;
    public GameObject explosionPrefab;

    [Header("Training Status")]
    public bool isVsAIMode = false;   // Set to true when playing the actual game
    public bool isWaitingForShot = false;
    private GameObject currentProjectile;

    public override void OnEpisodeBegin()
    {
        isWaitingForShot = false;

        // In training mode, we reset terrain every time. 
        // In Game mode, the VsAIManager handles setup.
        if (!isVsAIMode)
        {
            if (terrain != null)
            {
                terrain.GenerateTerrain(Random.Range(1, 10000), "desert");
            }

            // Left side [-8, -2], Right side [2, 8]
            localTank.transform.position = new Vector3(Random.Range(-8f, -2f), 0, 0);
            enemyTank.transform.position = new Vector3(Random.Range(2f, 8f), 0, 0);
            
            localTank.PlaceOnTerrain();
            enemyTank.PlaceOnTerrain();

            localTank.currentHp = localTank.maxHp;
            enemyTank.currentHp = enemyTank.maxHp;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 5 observations
        float ownX = localTank.transform.position.x;
        float enemyX = enemyTank.transform.position.x;

        sensor.AddObservation(ownX / 10f); // Normalized
        sensor.AddObservation(enemyX / 10f);
        sensor.AddObservation((enemyX - ownX) / 20f);
        sensor.AddObservation(localTank.currentHp / 100f);
        sensor.AddObservation(enemyTank.currentHp / 100f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (isWaitingForShot) return;

        float rawAngle = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float angle = ((rawAngle + 1f) / 2f) * 90f;

        float rawPower = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float power = ((rawPower + 1f) / 2f) * 100f;

        FireActualShot(angle, power);
        
        if (!isVsAIMode) AddReward(-0.001f);
    }

    public void FireActualShot(float angle, float power)
    {
        if (projectilePrefab == null) return;
        isWaitingForShot = true;

        Vector3 spawnPos = localTank.barrel != null
            ? localTank.barrel.position
            : localTank.transform.position + Vector3.up * 0.5f;

        GameObject proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        
        // Prevent projectile from hitting the AI tank firing it
        Collider2D projCol = proj.GetComponent<Collider2D>();
        Collider2D tankCol = localTank.GetComponent<Collider2D>();
        if (projCol != null && tankCol != null)
        {
            Physics2D.IgnoreCollision(projCol, tankCol);
        }

        ProjectileController pc = proj.GetComponent<ProjectileController>();

        bool facingRight = localTank.transform.position.x < enemyTank.transform.position.x;
        localTank.SetBarrelAngle(angle, facingRight);

        pc.SetImpactCallback(OnProjectileImpact);
        pc.Launch(angle, power, facingRight);
        currentProjectile = proj;
    }

    private void OnProjectileImpact(Vector2 impactWorld, bool hitTank)
    {
        isWaitingForShot = false;

        if (terrain != null)
        {
            terrain.DestroyTerrain(impactWorld, 0.8f);
            localTank.PlaceOnTerrain();
            enemyTank.PlaceOnTerrain();
        }

        if (explosionPrefab != null)
        {
            GameObject exp = Instantiate(explosionPrefab, new Vector3(impactWorld.x, impactWorld.y, 0f), Quaternion.identity);
            Destroy(exp, 2f);
        }

        if (isVsAIMode)
        {
            // Resolve damage in Game Mode
            if (hitTank) enemyTank.TakeDamage(35);
            else 
            {
                float dist = Vector2.Distance(impactWorld, enemyTank.transform.position);
                if (dist < 1.5f) enemyTank.TakeDamage(15);
            }
            // Notify Manager that resolution is done
            VsAIManager.Instance?.OnProjectileResolved();
        }
        else
        {
            // Training Mode Logic
            if (hitTank)
            {
                enemyTank.TakeDamage(40);
                AddReward(1.0f);
                EndEpisode();
            }
            else
            {
                float relX = impactWorld.x - terrain.transform.position.x;
                float relY = impactWorld.y - terrain.transform.position.y;
                
                if (relY < -9f || Mathf.Abs(relX) > 14f)
                {
                    AddReward(-1.0f);
                    EndEpisode();
                }
                else
                {
                    float dist = Vector2.Distance(impactWorld, (Vector2)enemyTank.transform.position);
                    if (dist <= 1.5f) { enemyTank.TakeDamage(15); AddReward(0.5f); }
                    else AddReward(-0.1f);
                    EndEpisode();
                }
            }
        }
    }

    // Used to test manually with Sliders (Heuristics)
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        // This is purely a placeholder. To truly use heuristics, developers 
        // will link UI sliders to this method or read input here.
        // Defaulting to 45 degree, 50 power
        continuousActionsOut[0] = 0f; // Maps to 45 deg
        continuousActionsOut[1] = 0f; // Maps to 50 power
    }
}
