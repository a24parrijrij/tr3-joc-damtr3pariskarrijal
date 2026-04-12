// Controla el projectil: físiques i detecció d'impacte
using UnityEngine;
using System;

public class ProjectileController : MonoBehaviour
{
    private Action<Vector2, bool> onImpact;
    private bool  hasImpacted = false;
    private Vector2 startPos;

    // Llança el projectil amb angle i potència donats
    public void Launch(float angleDeg, float power, bool facingRight)
    {
        startPos = transform.position;
        
        float sign    = facingRight ? 1f : -1f;
        float radians = angleDeg * Mathf.Deg2Rad;
        float speed   = power * 0.12f;

        var rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;

        rb.linearVelocity = new Vector2(
            Mathf.Cos(radians) * speed * sign,
            Mathf.Sin(radians) * speed
        );
    }

    // Registra el callback que s'executarà quan impacti
    public void SetImpactCallback(Action<Vector2, bool> callback)
    {
        onImpact = callback;
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (hasImpacted) return;
        hasImpacted = true;

        bool hitTank = col.gameObject.CompareTag("Tank");
        Vector2 pos  = transform.position;

        onImpact?.Invoke(pos, hitTank);
        Destroy(gameObject);
    }

    // Destrueix el projectil si surt del camp de joc
    void Update()
    {
        if (transform.position.y < startPos.y - 15f || Mathf.Abs(transform.position.x - startPos.x) > 20f)
        {
            onImpact?.Invoke(transform.position, false);
            Destroy(gameObject);
        }
    }
}
