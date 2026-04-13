// HumanTankInput — Player input in Vs AI mode.
// Reads angle/power from the SAME UXML sliders as CombatManager (no raw keyboard).
// Move Left / Move Right buttons are handled directly by VsAIManager; this
// component keeps the barrel angle in sync with the angle slider every frame
// and lets Space bar fire (same feel as the combat screen fire button).
using UnityEngine;
using UnityEngine.UIElements;

public class HumanTankInput : MonoBehaviour
{
    [Header("References")]
    public TankController tank;
    public VsAIManager    manager;

    // Cache sliders once
    private Slider angleSlider;
    private Slider powerSlider;
    private bool   slidersFound = false;

    void Start()
    {
        TryFindSliders();
    }

    private void TryFindSliders()
    {
        if (slidersFound || manager == null) return;
        var doc = manager.GetComponent<UIDocument>();
        if (doc == null) return;
        var root   = doc.rootVisualElement;
        angleSlider = root.Q<Slider>("angle-slider");
        powerSlider = root.Q<Slider>("power-slider");
        slidersFound = (angleSlider != null && powerSlider != null);
    }

    void Update()
    {
        if (tank == null || manager == null) return;
        if (!manager.IsPlayerTurn()) return;

        if (!slidersFound) TryFindSliders();

        // ── Read current angle/power from UI sliders ───────────────────────
        float currentAngle = angleSlider != null ? angleSlider.value : 45f;
        float currentPower = powerSlider != null ? powerSlider.value : 75f;

        // ── Keep barrel visual in sync with the slider ─────────────────────
        if (tank != null && manager.aiTank != null)
        {
            bool facingRight = tank.transform.position.x < manager.aiTank.transform.position.x;
            tank.SetBarrelAngle(currentAngle, facingRight);
        }

        // ── Keyboard shortcuts (optional, same result as the UI controls) ──

        // A/D or Left/Right → move (delegates to VsAIManager same as button presses)
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            if (tank.CanStillMove())
            {
                tank.Move(-1f, Time.deltaTime);
                tank.PlaceOnTerrain();
            }
        }
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            if (tank.CanStillMove())
            {
                tank.Move(1f, Time.deltaTime);
                tank.PlaceOnTerrain();
            }
        }

        // W/S or Up/Down → adjust angle slider
        float angleDir = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))   angleDir =  1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) angleDir = -1f;
        if (Mathf.Abs(angleDir) > 0.01f && angleSlider != null)
            angleSlider.value = Mathf.Clamp(angleSlider.value + angleDir * 45f * Time.deltaTime, 0f, 90f);

        // Q/E → adjust power slider (also support Shift/Ctrl)
        float powerDir = 0f;
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.Equals)) powerDir =  1f;
        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.Minus)) powerDir = -1f;
        if (Mathf.Abs(powerDir) > 0.01f && powerSlider != null)
            powerSlider.value = Mathf.Clamp(powerSlider.value + powerDir * 50f * Time.deltaTime, 0f, 100f);

        // Space → fire (mirrors clicking the fire button)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            manager.PlayerFires(
                angleSlider != null ? angleSlider.value : 45f,
                powerSlider != null ? powerSlider.value : 75f);
        }
    }
}