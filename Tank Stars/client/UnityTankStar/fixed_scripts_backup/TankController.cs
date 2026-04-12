// Controla un tanc: HP, moviment sobre el terreny i visualització
using UnityEngine;

public class TankController : MonoBehaviour
{
    [Header("Identificació")]
    public int  playerId;
    public bool isLocalPlayer;

    [Header("Components")]
    public Transform        barrel;
    public TerrainGenerator terrain;

    [Header("Stats")]
    public int   currentHp    = 100;
    public int   maxHp        = 100;

    [Header("Moviment")]
    public float moveSpeed        = 4f;   // world units per second
    public float maxMovePerTurn   = 2.5f; // total distance allowed per turn
    public float worldBoundsX     = 9.5f; // can't go beyond ±this

    // Tracking
    private float _distanceMovedThisTurn = 0f;
    public  float DistanceMovedThisTurn => _distanceMovedThisTurn;

    // ─── Terrain placement ────────────────────────────────────────────────

    /// <summary>Snaps the tank to sit exactly on the terrain surface.</summary>
    public void PlaceOnTerrain()
    {
        if (terrain == null) return;
        // GetHeightAtX now returns WORLD Y — just add a small half-body offset
        float worldY = terrain.GetHeightAtX(transform.position.x);
        transform.position = new Vector3(transform.position.x, worldY + 0.35f, 0f);
    }

    // ─── Turn management ─────────────────────────────────────────────────

    /// <summary>Call at the start of this tank's turn to reset the movement budget.</summary>
    public void StartTurn() => _distanceMovedThisTurn = 0f;

    // ─── Movement ─────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the tank horizontally, following the terrain surface.
    /// Returns the actual distance moved (may be less than requested if at limit or world edge).
    /// direction: -1 (left) to +1 (right).
    /// </summary>
    public float Move(float direction, float deltaTime)
    {
        float remaining = maxMovePerTurn - _distanceMovedThisTurn;
        if (remaining <= 0f) return 0f;

        float step   = direction * moveSpeed * deltaTime;
        float capped = Mathf.Clamp(step, -remaining, remaining);

        float newX = Mathf.Clamp(transform.position.x + capped, -worldBoundsX, worldBoundsX);
        float actualStep = newX - transform.position.x;

        if (Mathf.Abs(actualStep) < 0.0001f) return 0f;

        // Move and snap to terrain
        transform.position = new Vector3(newX, transform.position.y, 0f);
        PlaceOnTerrain();

        _distanceMovedThisTurn += Mathf.Abs(actualStep);
        return actualStep;
    }

    /// <summary>Checks whether this tank has used up its movement budget.</summary>
    public bool CanStillMove() => _distanceMovedThisTurn < maxMovePerTurn;

    // ─── Combat ──────────────────────────────────────────────────────────

    /// <summary>Applies damage to the tank (cannot go below 0).</summary>
    public void TakeDamage(int amount)
    {
        currentHp = Mathf.Max(0, currentHp - amount);
    }

    public bool IsAlive => currentHp > 0;

    // ─── Barrel ──────────────────────────────────────────────────────────

    /// <summary>Rotates the barrel to the given angle in degrees.</summary>
    public void SetBarrelAngle(float angleDegrees, bool facingRight)
    {
        if (barrel == null) return;
        float sign = facingRight ? 1f : -1f;
        barrel.localRotation = Quaternion.Euler(0f, 0f, angleDegrees * sign);
    }
}
