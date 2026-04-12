// Gestor global del joc — persisteix entre escenes
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // Dades d'autenticació
    public string authToken;
    public int playerId;
    public string username;

    // Dades de la partida
    public int gameId;
    public string roomCode;
    public string mapType = "desert";

    // Reinicia l'estat de la partida sense esborrar les dades d'usuari
    public void ResetMatchState()
    {
        gameId   = 0;
        roomCode = string.Empty;
    }

    // Retorna la instància existent o en crea una de nova
    public static GameManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        var existing = FindFirstObjectByType<GameManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("GameManager");
        Instance = go.AddComponent<GameManager>();
        return Instance;
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
