// Assets/Scripts/FirestoreWebViewManager.cs
using System;
using System.Collections;
using UnityEngine;
using Firebase.Firestore; // можно убрать, если не нужен тихий refresh START_URL

namespace App.Web
{
    public class FirestoreWebViewManager : MonoBehaviour
    {
        private WebViewObject webView;

        // PlayerPrefs keys
        private const string START_URL_KEY        = "webview_start_url";
        private const string LAST_URL_KEY         = "webview_last_url";
        private const string WEBVIEW_UNLOCKED_KEY = "webview_unlocked";

        [Header("URLs")]
        [Tooltip("Запасной URL на случай, если нет ни LAST, ни START")]
        public string emergencyUrl = "";

        [Header("Options")]
        [Tooltip("HTML preconnect + мгновенный redirect (может ускорить, по умолчанию выкл.)")]
        public bool usePreconnect = false;
        [Tooltip("Подменить User-Agent на мобильный (через JNI, без SetUserAgent в плагине)")]
        public bool setMobileUserAgent = true;
        [Tooltip("Разрешить http (ТОЛЬКО для отладки; куки SameSite=None не будут работать)")]
        public bool allowHttpForDebug = false;

        [Header("Cookies/Storage")]
        [Tooltip("Включить cookies/3rd-party/DOM storage до первой загрузки")]
        public bool enableCookiesBeforeLoad = true;
        [Tooltip("Блокирующий таймаут включения кук (сек)")]
        public float cookieSetupTimeout = 0.6f;
        [Tooltip("Период опроса UI-потока при включении кук (сек)")]
        public float cookieSetupPoll = 0.1f;

#if UNITY_ANDROID
        private const string MOBILE_UA =
            "Mozilla/5.0 (Linux; Android 13; Mobile) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Mobile Safari/537.36";
#endif

        private string initialCandidate;
        private bool   loadedOnce;

        private IEnumerator Start()
        {
            // 1) создаём WebView (скрыт до первой нормальной загрузки)
            webView = new GameObject("WebViewObject").AddComponent<WebViewObject>();
            webView.Init(
                cb:      OnJsMessage,
                err:     OnError,
                started: u => Debug.Log("[WebView] Started: " + u),
                ld:      OnLoaded
            );
            webView.SetMargins(0, 0, 0, 0);
            webView.SetTextZoom(100);
            webView.SetVisibility(false);

            yield return null; // один кадр после Init (важно для JNI-доступа к View)

            // 2) поставить мобильный UA (в твоём плагине нет SetUserAgent — делаем через JNI)
            if (setMobileUserAgent) TrySetMobileUserAgentJNI();

            // 3) cookies/DOM storage до первой загрузки (коротко и блокирующе)
            if (enableCookiesBeforeLoad)
                yield return EnsureCookiesSafe(cookieSetupTimeout, cookieSetupPoll);

            // 4) выбираем, что грузить: LAST → START → emergency
            string candidate = PlayerPrefs.GetString(LAST_URL_KEY, string.Empty);
            if (!IsValidStartUrl(candidate)) candidate = PlayerPrefs.GetString(START_URL_KEY, string.Empty);
            if (!IsValidStartUrl(candidate)) candidate = emergencyUrl;

            initialCandidate = candidate;

            if (IsValidStartUrl(candidate))
            {
                if (usePreconnect) LoadWithPreconnect(candidate);
                else               webView.LoadURL(candidate);
            }
            else
            {
                Debug.LogWarning("[WebView] Нет ни LAST, ни START, ни emergency URL");
            }

            // 5) (опц.) тихо освежим START_URL из Firestore (Cache → Server), не сбивая загрузку
            var db = FirebaseFirestore.DefaultInstance;
            var docRef = db.Collection("links").Document("sweetbonanza");

            // Cache
            var cacheTask = docRef.GetSnapshotAsync(Source.Cache);
            yield return new WaitUntil(() => cacheTask.IsCompleted);
            if (cacheTask.Exception == null && cacheTask.Result != null && cacheTask.Result.Exists && cacheTask.Result.ContainsField("samsung"))
            {
                var cachedUrl = cacheTask.Result.GetValue<string>("samsung");
                if (IsValidStartUrl(cachedUrl))
                {
                    PlayerPrefs.SetString(START_URL_KEY, cachedUrl);
                    PlayerPrefs.Save();
                    if (!loadedOnce && !IsValidStartUrl(initialCandidate))
                    {
                        if (usePreconnect) LoadWithPreconnect(cachedUrl); else webView.LoadURL(cachedUrl);
                    }
                }
            }

            // Server
            var netTask = docRef.GetSnapshotAsync(Source.Server);
            yield return new WaitUntil(() => netTask.IsCompleted);
            if (netTask.Exception == null && netTask.Result != null && netTask.Result.Exists && netTask.Result.ContainsField("samsung"))
            {
                var freshUrl = netTask.Result.GetValue<string>("samsung");
                if (IsValidStartUrl(freshUrl))
                {
                    PlayerPrefs.SetString(START_URL_KEY, freshUrl);
                    PlayerPrefs.Save();
                    if (!loadedOnce && !IsValidStartUrl(initialCandidate))
                    {
                        if (usePreconnect) LoadWithPreconnect(freshUrl); else webView.LoadURL(freshUrl);
                    }
                }
            }
        }

        private void OnLoaded(string url)
        {
            loadedOnce = true;

            // Липкий флаг: WebView успешно открывался
            PlayerPrefs.SetInt(WEBVIEW_UNLOCKED_KEY, 1);
            PlayerPrefs.Save();

            SaveLastUrl(url);

            webView.SetVisibility(true);
        }

        private void OnError(string err)
        {
            Debug.LogError("[WebView] Error: " + err);

            // Попробуем перезагрузить START_URL, затем emergency
            var start = PlayerPrefs.GetString(START_URL_KEY, string.Empty);
            if (IsValidStartUrl(start)) { webView.LoadURL(start); return; }

            if (IsValidStartUrl(emergencyUrl)) webView.LoadURL(emergencyUrl);
        }

        private void OnJsMessage(string msg)
        {
            // Пока что не используем JS-сообщения для определения back-навигации
            // Полагаемся только на webView.CanGoBack()
        }

        private void SaveLastUrl(string url)
        {
            if (IsValidStartUrl(url) && !LooksLikeAuth(url))
            {
                PlayerPrefs.SetString(LAST_URL_KEY, url);
                PlayerPrefs.Save();
            }
        }

        private void Update()
        {
            #if ENABLE_INPUT_SYSTEM
            if (Application.platform == RuntimePlatform.Android && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HandleBackNavigation();
            }
            #else
            if (Application.platform == RuntimePlatform.Android && Input.GetKeyDown(KeyCode.Escape))
            {
                HandleBackNavigation();
            }
            #endif
        }

        public void Back()
        {
            HandleBackNavigation();
        }

        private void HandleBackNavigation()
        {
            if (webView == null) 
            {
                Application.Quit(); 
                return; 
            }

            // Простая логика: если есть история в WebView, возвращаемся. Иначе — выходим.
            if (webView.CanGoBack())
            {
                webView.GoBack();
            }
            else
            {
                Application.Quit();
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause) StartCoroutine(FlushCookiesUI());
        }

        private void OnApplicationQuit()
        {
            StartCoroutine(FlushCookiesUI());
        }

        // ---------- helpers ----------

        private bool IsValidStartUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.StartsWith("https://")) return true;
            if (allowHttpForDebug && s.StartsWith("http://")) return true;
            return false;
        }

        private static bool LooksLikeAuth(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            var u = url.ToLowerInvariant();
            return u.Contains("login") || u.Contains("auth") || u.Contains("signin") || u.Contains("otp") || u.Contains("callback");
        }

        private void LoadWithPreconnect(string url)
        {
            try
            {
                var u = new Uri(url);
                var origin = $"{u.Scheme}://{u.Host}";
                string html = $@"<!doctype html><meta charset=utf-8>
<meta http-equiv='x-dns-prefetch-control' content='on'>
<link rel='preconnect' href='{origin}' crossorigin>
<link rel='dns-prefetch' href='//{u.Host}'>
<script>setTimeout(function(){{ location.replace('{url}'); }}, 50);</script>";
                webView.LoadHTML(html, origin);
            }
            catch { webView.LoadURL(url); }
        }

        // ===== JNI cookies / UA / storage (Android) =====
#if UNITY_ANDROID && !UNITY_EDITOR
        private void TrySetMobileUserAgentJNI()
        {
            try
            {
                var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                        using (var decor  = window.Call<AndroidJavaObject>("getDecorView"))
                        {
                            var wv = FindWebViewRecursive(decor);
                            if (wv != null)
                            {
                                var settings = wv.Call<AndroidJavaObject>("getSettings");
                                settings.Call("setUserAgentString", MOBILE_UA);
                            }
                        }
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private IEnumerator EnsureCookiesSafe(float timeoutSec, float pollSec)
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
                            var wv = FindWebViewRecursive(decor);
                            if (wv != null)
                            {
                                var cmClass = new AndroidJavaClass("android.webkit.CookieManager");
                                var cm      = cmClass.CallStatic<AndroidJavaObject>("getInstance");
                                cm.Call("setAcceptCookie", true);

                                int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
                                if (sdk >= 21) cm.Call("setAcceptThirdPartyCookies", wv, true);

                                var settings = wv.Call<AndroidJavaObject>("getSettings");
                                settings.Call("setDomStorageEnabled", true);
                                settings.Call("setDatabaseEnabled",  true);
                                settings.Call("setLoadsImagesAutomatically", true);
                                settings.Call("setBlockNetworkImage", false);
                                settings.Call("setCacheMode", 1 /* LOAD_CACHE_ELSE_NETWORK */);
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

        private IEnumerator FlushCookiesUI()
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

        private AndroidJavaObject FindWebViewRecursive(AndroidJavaObject view)
        {
            if (view == null) return null;

            // Если у view есть getSettings() — это WebView
            try { view.Call<AndroidJavaObject>("getSettings"); return view; } catch { }

            // Иначе обходим детей (ViewGroup)
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
        // Стабы для не-Android/Editor
        private void TrySetMobileUserAgentJNI() {}
        private IEnumerator EnsureCookiesSafe(float a, float b) { yield break; }
        private IEnumerator FlushCookiesUI() { yield break; }
        private AndroidJavaObject FindWebViewRecursive(AndroidJavaObject view) { return null; }
#endif
    }
}
