using System.Collections;
using UnityEngine;

public static class AndroidCookieSetup
{
#if UNITY_ANDROID && !UNITY_EDITOR
    public static IEnumerator EnsureCookies(float timeoutSec = 1.0f, float pollSec = 0.1f)
    {
        float t = 0f;
        var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

        while (t < timeoutSec)
        {
            bool uiDone = false;
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                    using (var decor  = window.Call<AndroidJavaObject>("getDecorView"))
                    {
                        var webView = FindWebViewRecursive(decor);
                        if (webView != null)
                        {
                            var cmClass = new AndroidJavaClass("android.webkit.CookieManager");
                            var cm      = cmClass.CallStatic<AndroidJavaObject>("getInstance");
                            cm.Call("setAcceptCookie", true);

                            int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
                            if (sdk >= 21) cm.Call("setAcceptThirdPartyCookies", webView, true);

                            var settings = webView.Call<AndroidJavaObject>("getSettings");
                            settings.Call("setDomStorageEnabled", true);
                            settings.Call("setDatabaseEnabled",  true);
                            // ускоряем повторные заходы: кэш из сети, если есть
                            settings.Call("setCacheMode", 1 /*LOAD_CACHE_ELSE_NETWORK*/);
                            settings.Call("setLoadsImagesAutomatically", true);
                            settings.Call("setBlockNetworkImage", false);
                        }
                    }
                }
                catch { }
                uiDone = true;
            }));
            while (!uiDone) yield return null;

            t += pollSec;
            yield return new WaitForSecondsRealtime(pollSec);
        }
    }

    public static void SetCookie(string url, string cookie)
    {
        try
        {
            var cm = new AndroidJavaClass("android.webkit.CookieManager").CallStatic<AndroidJavaObject>("getInstance");
            cm.Call("setCookie", url, cookie);
        }
        catch { }
    }

    public static IEnumerator FlushCookiesUI()
    {
        var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        bool done = false;
        activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            try
            {
                var cm = new AndroidJavaClass("android.webkit.CookieManager").CallStatic<AndroidJavaObject>("getInstance");
                cm.Call("flush");
            }
            catch { }
            done = true;
        }));
        while (!done) yield return null;
    }

    private static AndroidJavaObject FindWebViewRecursive(AndroidJavaObject view)
    {
        if (view == null) return null;
        try { view.Call<AndroidJavaObject>("getSettings"); return view; } catch { }
        try
        {
            int count = view.Call<int>("getChildCount");
            for (int i = 0; i < count; i++)
            {
                var child = view.Call<AndroidJavaObject>("getChildAt", i);
                var found = FindWebViewRecursive(child);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }
#else
    public static IEnumerator EnsureCookies(float timeoutSec = 1.0f, float pollSec = 0.1f) { yield break; }
    public static void SetCookie(string url, string cookie) { }
    public static IEnumerator FlushCookiesUI() { yield break; }
#endif
}