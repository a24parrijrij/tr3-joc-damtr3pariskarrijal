// Controla el projectil: físiques i detecció d'impacte
using UnityEngine;
using System;

public class ProjectileController : MonoBehaviour
{
    private Action<Vector2, bool> onImpact;
    private bool  hasImpacted = false;
    private Vector2 startPos;

    // launches the projectile with the given angle and power
    // facingRight flips the horizontal direction so it works for both players
    public void Launch(float angleDeg, float power, bool facingRight)
    {
        startPos = transform.position;

        float sign    = facingRight ? 1f : -1f;
        float radians = angleDeg * Mathf.Deg2Rad;
        float speed   = power * 0.12f;

        var rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;

        // set the velocity using cos/sin so the angle actually means something
        rb.linearVelocity = new Vector2(
            Mathf.Cos(radians) * speed * sign,
            Mathf.Sin(radians) * speed
        );
    }

    // register the callback that fires when the projectile hits something
    public void SetImpactCallback(Action<Vector2, bool> callback)
    {
        onImpact = callback;
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        // hasImpacted prevents the callback from firing twice if there are multiple collisions
        if (hasImpacted) return;
        hasImpacted = true;

        bool hitTank = col.gameObject.CompareTag("Tank");
        Vector2 pos  = transform.position;

        onImpact?.Invoke(pos, hitTank);
        Destroy(gameObject);
    }

    // destroy the projectile if it goes too far off screen
    void Update()
    {
        if (transform.position.y < startPos.y - 15f || Mathf.Abs(transform.position.x - startPos.x) > 20f)
        {
            onImpact?.Invoke(transform.position, false);
            Destroy(gameObject);
        }
    }
}
