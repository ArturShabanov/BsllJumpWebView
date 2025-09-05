using UnityEngine;

public class ScoreZone : MonoBehaviour
{
    private bool scored = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"ScoreZone Trigger: {other.name}, Tag: {other.tag}");
        if (!scored && other.CompareTag("Ball"))
        {
            scored = true;
            Debug.Log("Score! AddScore called");
            GameManager.Instance.AddScore(1);
        }
    }
} 