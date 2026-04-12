using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MLAgents;

public class VsAIManager : MonoBehaviour
{
    public static VsAIManager Instance;

    [Header("References")]
    public TerrainGenerator terrain;
    public TankController  playerTank;
    public TankController  aiTank;
    public TankAgent       aiAgent;
    
    [Header("Game State")]
    public bool isPlayerTurn = true;
    public bool isGameOver = false;
    public bool isResolutionInProgress = false;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        StartNewGame();
    }

    public void StartNewGame()
    {
        isGameOver = false;
        isPlayerTurn = true;
        isResolutionInProgress = false;

        // 1. Generate mountainous terrain (5 octaves as requested)
        int seed = Random.Range(1, 99999);
        terrain.GenerateTerrain(seed, "mountain");

        // 2. Position tanks and assign terrain reference
        playerTank.terrain = terrain;
        aiTank.terrain = terrain;
        
        playerTank.transform.position = new Vector3(-7f, 0, 0);
        aiTank.transform.position     = new Vector3(7f, 0, 0);
        
        playerTank.PlaceOnTerrain();
        aiTank.PlaceOnTerrain();

        // 3. Setup AI Agent
        aiAgent.isVsAIMode = true;
        
        // Reset HP
        playerTank.currentHp = playerTank.maxHp;
        aiTank.currentHp = aiTank.maxHp;

        UpdateTurnUI();
    }

    public bool IsPlayerTurn() => isPlayerTurn && !isGameOver && !isResolutionInProgress;

    public void PlayerFires(float angle, float power)
    {
        if (!IsPlayerTurn()) return;
        
        isResolutionInProgress = true;
        
        if (aiAgent.projectilePrefab == null) return;
        
        Vector3 spawnPos = playerTank.barrel != null
            ? playerTank.barrel.position
            : playerTank.transform.position + Vector3.up * 0.5f;

        GameObject proj = Instantiate(aiAgent.projectilePrefab, spawnPos, Quaternion.identity);
        
        // Prevent projectile from hitting the tank firing it
        Collider2D projCol = proj.GetComponent<Collider2D>();
        Collider2D tankCol = playerTank.GetComponent<Collider2D>();
        if (projCol != null && tankCol != null)
        {
            Physics2D.IgnoreCollision(projCol, tankCol);
        }

        ProjectileController pc = proj.GetComponent<ProjectileController>();

        bool facingRight = playerTank.transform.position.x < aiTank.transform.position.x;
        playerTank.SetBarrelAngle(angle, facingRight);

        pc.SetImpactCallback(OnPlayerProjectileImpact);
        pc.Launch(angle, power, facingRight);
    }

    private void OnPlayerProjectileImpact(Vector2 impactWorld, bool hitTank)
    {
        if (terrain != null) 
        {
            terrain.DestroyTerrain(impactWorld, 0.8f);
            playerTank.PlaceOnTerrain();
            aiTank.PlaceOnTerrain();
        }
        
        if (aiAgent.explosionPrefab != null)
        {
            GameObject exp = Instantiate(aiAgent.explosionPrefab, new Vector3(impactWorld.x, impactWorld.y, 0f), Quaternion.identity);
            Destroy(exp, 2f);
        }

        // Apply damage to AI Tank
        if (hitTank) aiTank.TakeDamage(35);
        else 
        {
            float dist = Vector2.Distance(impactWorld, aiTank.transform.position);
            if (dist < 1.5f) aiTank.TakeDamage(15);
        }
        
        OnProjectileResolved();
    }

    public void OnProjectileResolved()
    {
        isResolutionInProgress = false;

        // Check victory
        if (aiTank.currentHp <= 0)
        {
            isGameOver = true;
            Debug.Log("PLAYER WINS!");
            return;
        }
        if (playerTank.currentHp <= 0)
        {
            isGameOver = true;
            Debug.Log("AI WINS!");
            return;
        }

        // Switch turn
        isPlayerTurn = !isPlayerTurn;
        UpdateTurnUI();

        if (!isPlayerTurn && !isGameOver)
        {
            Invoke(nameof(StartAITurn), 1.0f);
        }
    }

    private void StartAITurn()
    {
        if (isGameOver) return;
        // Request decision from the trained model
        aiAgent.RequestDecision();
    }

    private void UpdateTurnUI()
    {
        // For now, log to console. A real HUD would update labels here.
        Debug.Log(isPlayerTurn ? "YOUR TURN" : "AI TURN");
    }

    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
