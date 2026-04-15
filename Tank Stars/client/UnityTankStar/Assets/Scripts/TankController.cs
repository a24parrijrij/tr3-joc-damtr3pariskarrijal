// TankController — Controla un tanc: HP, moviment sobre el terreny i rotació del canó
using UnityEngine;

public class TankController : MonoBehaviour
{
    [Header("Identificació")]
    public int  playerId;
    public bool isLocalPlayer;

    [Header("Components")]
    public Transform        barrel;
    public TerrainGenerator terrain;

    [Header("Estadístiques")]
    public int   currentHp    = 100;
    public int   maxHp        = 100;

    [Header("Moviment")]
    public float moveSpeed        = 4f;   // unitats mundials per segon
    public float maxMovePerTurn   = 2.5f; // distància total permesa per torn
    public float worldBoundsX     = 9.5f; // no pot anar més enllà de ±aquest valor

    // tracks how far the tank has moved this turn
    private float _distanceMovedThisTurn = 0f;
    public  float DistanceMovedThisTurn => _distanceMovedThisTurn;

    // cache the rigidbody so we don't call GetComponent every single frame (that's slow)
    private Rigidbody2D _rb;

    void Awake() { _rb = GetComponent<Rigidbody2D>(); }

    // ─── Col·locació al terreny ───────────────────────────────────────────

    // snaps the tank to the terrain surface
    // we also zero out the rigidbody velocity here because without it the tank
    // slides sideways off the screen after a crater is made under it (physics bug)
    public void PlaceOnTerrain()
    {
        if (terrain == null) return;
        float worldY = terrain.GetHeightAtX(transform.position.x);
        transform.position = new Vector3(transform.position.x, worldY + 0.35f, 0f);
        // reset velocity so the tank doesn't glide after being placed
        if (_rb != null) { _rb.linearVelocity = Vector2.zero; _rb.angularVelocity = 0f; }
    }

    // ─── Gestió de torns ─────────────────────────────────────────────────

    // call this at the start of each turn to reset the movement budget
    public void StartTurn() => _distanceMovedThisTurn = 0f;

    // ─── Moviment ─────────────────────────────────────────────────────────

    // moves the tank left (-1) or right (+1), returns actual distance moved
    // returns 0 if the movement budget for this turn is already used up
    public float Move(float direction, float deltaTime)
    {
        float remaining = maxMovePerTurn - _distanceMovedThisTurn;
        if (remaining <= 0f) return 0f;

        float step   = direction * moveSpeed * deltaTime;
        float capped = Mathf.Clamp(step, -remaining, remaining);

        float newX = Mathf.Clamp(transform.position.x + capped, -worldBoundsX, worldBoundsX);
        float actualStep = newX - transform.position.x;

        if (Mathf.Abs(actualStep) < 0.0001f) return 0f;

        // Slope check — block movement only on near-vertical cliffs (> 55°).
        // The tank is long so it rides moderate slopes naturally; a tight threshold
        // blocks movement on regular hills and feels broken.
        if (terrain != null && Mathf.Abs(actualStep) > 0.01f)
        {
            float currentSurfaceY = transform.position.y - 0.35f;
            float newSurfaceY     = terrain.GetHeightAtX(newX);
            float slopeAngle      = Mathf.Abs(Mathf.Atan2(newSurfaceY - currentSurfaceY,
                                                           Mathf.Abs(actualStep)) * Mathf.Rad2Deg);
            if (slopeAngle > 65f) return 0f;
        }

        // calculate the Y at the NEW x position before actually moving the tank
        // if we move X first and then fix Y, the collider briefly clips into steep terrain
        // and the physics engine pushes the tank sideways — this was causing the movement glitch
        float newY = terrain != null ? terrain.GetHeightAtX(newX) + 0.35f : transform.position.y;
        transform.position = new Vector3(newX, newY, 0f);
        if (_rb != null) { _rb.linearVelocity = Vector2.zero; _rb.angularVelocity = 0f; }

        _distanceMovedThisTurn += Mathf.Abs(actualStep);
        return actualStep;
    }

    // returns true if the tank still has movement budget left this turn
    public bool CanStillMove() => _distanceMovedThisTurn < maxMovePerTurn;

    // ─── Combat ──────────────────────────────────────────────────────────

    // apply damage to the tank, hp can't go below 0
    public void TakeDamage(int amount)
    {
        currentHp = Mathf.Max(0, currentHp - amount);
        if (currentHp <= 0)
        {
            // place on terrain when dead just in case it's floating
            PlaceOnTerrain();
        }
    }

    public bool IsAlive => currentHp > 0;

    // ─── Canó ────────────────────────────────────────────────────────────

    // rotates the barrel to the given angle in degrees
    // facingRight flips the sign so it works for both player orientations
    public void SetBarrelAngle(float angleDegrees, bool facingRight)
    {
        if (barrel == null) return;
        float sign = facingRight ? 1f : -1f;
        barrel.localRotation = Quaternion.Euler(0f, 0f, angleDegrees * sign);
    }
}
