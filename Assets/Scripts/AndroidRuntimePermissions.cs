using System.Collections;
using UnityEngine;

public class AndroidRuntimePermissions : MonoBehaviour
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private const string READ_MEDIA_IMAGES = "android.permission.READ_MEDIA_IMAGES";
    private const string READ_EXTERNAL_STORAGE = "android.permission.READ_EXTERNAL_STORAGE";

    public static bool HasMediaPermission()
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var contextCompat = new AndroidJavaClass("androidx.core.content.ContextCompat"))
        {
            int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
            string perm = sdk >= 33 ? READ_MEDIA_IMAGES : READ_EXTERNAL_STORAGE;
            int res = contextCompat.CallStatic<int>("checkSelfPermission", activity, perm);
            return res == 0; // PackageManager.PERMISSION_GRANTED
        }
    }

    public static void RequestMediaPermission(System.Action<bool> callback)
    {
        var go = new GameObject("_AndroidPermReq");
        DontDestroyOnLoad(go);
        var c = go.AddComponent<AndroidRuntimePermissions>();
        c.StartCoroutine(c.RequestCoroutine(callback));
    }

    private IEnumerator RequestCoroutine(System.Action<bool> callback)
    {
        bool granted = false;
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var activityCompat = new AndroidJavaClass("androidx.core.app.ActivityCompat"))
        {
            int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
            string perm = sdk >= 33 ? READ_MEDIA_IMAGES : READ_EXTERNAL_STORAGE;

            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                activityCompat.CallStatic("requestPermissions", activity, new string[] { perm }, 12345);
            }));
        }

        // Poll result briefly (simple approach)
        float t = 0f;
        while (t < 3f)
        {
            if (HasMediaPermission()) { granted = true; break; }
            t += 0.2f;
            yield return new WaitForSecondsRealtime(0.2f);
        }

        callback?.Invoke(granted);
        Destroy(gameObject);
    }
#else
    public static bool HasMediaPermission() { return true; }
    public static void RequestMediaPermission(System.Action<bool> callback) { callback?.Invoke(true); }
#endif
}


