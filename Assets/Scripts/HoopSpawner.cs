using UnityEngine;

public class HoopSpawner : MonoBehaviour
{
    public GameObject hoopPrefab;
    public float spawnInterval = 1.5f;
    public float minY = -2f;
    public float maxY = 2f;

    void Start()
    {
        InvokeRepeating(nameof(SpawnHoop), 1f, spawnInterval);
    }

    void SpawnHoop()
    {
        float y = Random.Range(minY, maxY);
        Vector3 spawnPos = new Vector3(6f, y, 0f);
        Instantiate(hoopPrefab, spawnPos, Quaternion.identity);
    }
} 