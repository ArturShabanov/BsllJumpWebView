using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    public int score = 0;
    public bool isGameOver = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void AddScore(int value)
    {
        if (isGameOver) return;
        score += value;
        Debug.Log("AddScore: " + score);
        UIManager.Instance.UpdateScore(score);
    }

    public void GameOver()
    {
        isGameOver = true;
        UIManager.Instance.ShowGameOver();
        Time.timeScale = 0f;
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
} 