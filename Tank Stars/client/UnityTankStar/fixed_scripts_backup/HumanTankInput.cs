using UnityEngine;
using UnityEngine.InputSystem;

public class HumanTankInput : MonoBehaviour
{
    public TankController tank;
    public VsAIManager manager;
    
    [Header("Input Settings")]
    public float angleSpeed = 45f;
    public float powerChargeSpeed = 50f;
    
    [Header("State")]
    public float currentAngle = 45f;
    public float currentPower = 0f;
    private bool isCharging = false;
    private bool isFacingRight = true;
    
    private GameObject[] arcDots;
    private int maxDots = 12; // 12 dots will cover roughly 30-40% of the long trajectory

    void Start()
    {
        // Initialize dotted array
        arcDots = new GameObject[maxDots];
        for (int i = 0; i < maxDots; i++)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dot.GetComponent<Collider>()); // No physics collisions
            dot.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f); // Tiny dot
            
            // Make dot slightly transparent white
            MeshRenderer mr = dot.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Sprites/Default"));
            mr.material.color = new Color(1f, 1f, 1f, 1f - (i * 0.05f)); // Fades out towards the end
            
            dot.SetActive(false);
            arcDots[i] = dot;
        }
    }

    void Update()
    {
        if (tank == null || manager == null) return;
        if (!manager.IsPlayerTurn()) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        // 1. Movement (A/D or Left/Right)
        float moveDir = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) moveDir = -1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) moveDir = 1f;

        if (Mathf.Abs(moveDir) > 0.1f)
        {
            tank.Move(moveDir, Time.deltaTime);
        }

        // 2. Adjust Angle (W/S or Up/Down)
        float angleDir = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) angleDir = 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) angleDir = -1f;

        currentAngle = Mathf.Clamp(currentAngle + angleDir * angleSpeed * Time.deltaTime, 0f, 90f);
        
        // Update visual barrel
        isFacingRight = tank.transform.position.x < manager.aiTank.transform.position.x;
        tank.SetBarrelAngle(currentAngle, isFacingRight);

        // 3. Power and Fire (Space)
        if (kb.spaceKey.wasPressedThisFrame)
        {
            isCharging = true;
            currentPower = 0f;
            SetDotsActive(true);
        }

        if (isCharging)
        {
            currentPower = Mathf.Min(currentPower + powerChargeSpeed * Time.deltaTime, 100f);
            UpdateTrajectoryArc();
            
            if (kb.spaceKey.wasReleasedThisFrame)
            {
                isCharging = false;
                SetDotsActive(false);
                manager.PlayerFires(currentAngle, currentPower);
                currentPower = 0f;
            }
        }
    }

    private void SetDotsActive(bool active)
    {
        if (arcDots == null) return;
        foreach (var dot in arcDots)
        {
            if (dot != null) dot.SetActive(active);
        }
    }

    private void UpdateTrajectoryArc()
    {
        if (arcDots == null || tank.barrel == null) return;

        Vector3 startPos = tank.barrel.position;
        float sign = isFacingRight ? 1f : -1f;
        float radians = currentAngle * Mathf.Deg2Rad;
        float speed = currentPower * 0.12f;

        Vector2 velocity = new Vector2(
            Mathf.Cos(radians) * speed * sign,
            Mathf.Sin(radians) * speed
        );

        Vector2 gravity = Physics2D.gravity;
        Vector2 currentPos = startPos;
        
        // We space the dots by time. 0.15s per dot spreads them out nicely into a clear dotted line
        float timeStep = 0.15f; 
        
        for (int i = 0; i < maxDots; i++)
        {
            float t = i * timeStep;
            Vector2 p = currentPos + velocity * t + 0.5f * gravity * t * t;
            
            if (arcDots[i] != null)
            {
                arcDots[i].transform.position = new Vector3(p.x, p.y, 0f);
                
                // Hide dots that go heavily underground
                if (manager.terrain != null && p.y < manager.terrain.GetHeightAtX(p.x) - 1f)
                    arcDots[i].SetActive(false);
                else
                    arcDots[i].SetActive(true);
            }
        }
    }
}
