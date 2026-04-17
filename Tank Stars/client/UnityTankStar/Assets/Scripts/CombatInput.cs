// CombatInput — Controla el tanc local en mode multijugador vs jugador
// Controls: W/S (angle), Q/E (potència), A/D (moure), Espai (disparar)
using UnityEngine;
using UnityEngine.UIElements;

public class CombatInput : MonoBehaviour
{
    [Header("References")]
    public CombatManager manager;

    private Slider angleSlider;
    private Slider powerSlider;
    private bool slidersFound = false;

    void Start()
    {
        TryFindSliders();
    }

    private void TryFindSliders()
    {
        if (slidersFound || manager == null) return;
        var doc = manager.GetComponent<UIDocument>();
        if (doc == null) return;
        var root = doc.rootVisualElement;
        angleSlider = root.Q<Slider>("angle-slider");
        powerSlider = root.Q<Slider>("power-slider");
        slidersFound = (angleSlider != null && powerSlider != null);
    }

    void Update()
    {
        if (manager == null) return;
        if (!manager.IsMyTurn()) return;

        if (!slidersFound) TryFindSliders();

        float currentAngle = angleSlider != null ? angleSlider.value : 45f;
        float currentPower = powerSlider != null ? powerSlider.value : 75f;

        // Sincronitzar angle del canó amb el slider
        var localTank = manager.LocalTank;
        var remoteTank = manager.RemoteTank;
        if (localTank != null && remoteTank != null)
        {
            bool facingRight = localTank.transform.position.x < remoteTank.transform.position.x;
            localTank.SetBarrelAngle(currentAngle, facingRight);
        }

        // A/D o Esquerra/Dreta -> moure el tanc local
        bool moved = false;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            var tank = manager.LocalTank;
            if (tank != null && tank.CanStillMove())
            {
                tank.Move(-1f, Time.deltaTime);
                tank.PlaceOnTerrain();
                moved = true;
            }
        }
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            var tank = manager.LocalTank;
            if (tank != null && tank.CanStillMove())
            {
                tank.Move(1f, Time.deltaTime);
                tank.PlaceOnTerrain();
                moved = true;
            }
        }
        // Notify server on every frame of movement so the opponent sees real-time updates
        if (moved) manager.SendTankPosition();

        // W/S o Amunt/Avall -> ajustar slider d'angle
        float angleDir = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) angleDir = 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) angleDir = -1f;
        if (Mathf.Abs(angleDir) > 0.01f && angleSlider != null)
            angleSlider.value = Mathf.Clamp(angleSlider.value + angleDir * 45f * Time.deltaTime, 0f, 90f);

        // Q/E -> ajustar slider de potència
        float powerDir = 0f;
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.Equals)) powerDir = 1f;
        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.Minus)) powerDir = -1f;
        if (Mathf.Abs(powerDir) > 0.01f && powerSlider != null)
            powerSlider.value = Mathf.Clamp(powerSlider.value + powerDir * 50f * Time.deltaTime, 0f, 100f);

        // Espai -> disparar
        if (Input.GetKeyDown(KeyCode.Space))
        {
            manager.FireShot(
                angleSlider != null ? angleSlider.value : 45f,
                powerSlider != null ? powerSlider.value : 75f);
        }
    }
}
