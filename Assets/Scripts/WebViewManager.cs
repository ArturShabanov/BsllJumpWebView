using System.Collections;
using UnityEngine;
using Firebase.Firestore;

public class WebViewManager : MonoBehaviour
{
    private WebViewObject webView;

    [Header("View")] public bool showAfterFirstLoad = true;
    [Tooltip("Сдвиг показа после Init, сек")] public float initialDelay = 0.1f;
    [Tooltip("Учитывать safe area экрана")] public bool respectSafeArea = true;

    private const string START_URL_KEY = "webview_start_url";
    private const string LAST_URL_KEY  = "webview_last_url";

    private string baseUrl;
    private string baseDomain;
    private string initialLoadedUrl;
    private bool   userHasInteracted;
    private int    initialHistoryLen = -1;
    private int    currentHistoryLen = -1;

    private void Start()
    {
        StartCoroutine(InitAndLoad());
    }

    private IEnumerator InitAndLoad()
    {
        webView = (new GameObject("WebViewObject")).AddComponent<WebViewObject>();
        webView.Init(
            cb: OnJSMessage,
            err: (e) => { Debug.LogError("WV Error: " + e); TryFallback(); },
            started: (u) => Debug.Log("WV Started: " + u),
            ld: OnLoaded
        );

        webView.SetTextZoom(100);
        ApplySafeMargins();
        webView.SetVisibility(!showAfterFirstLoad);

        yield return null; // 1 кадр после Init
        yield return new WaitForSecondsRealtime(initialDelay);

        // Порядок выбора URL: last -> start -> Firestore
        string candidate = PlayerPrefs.GetString(LAST_URL_KEY, string.Empty);
        if (!IsLoadCandidateValid(candidate))
            candidate = PlayerPrefs.GetString(START_URL_KEY, string.Empty);

        if (IsValidHttp(candidate))
        {
            baseUrl = PlayerPrefs.GetString(START_URL_KEY, candidate);
            baseDomain = ExtractDomain(baseUrl);
            webView.LoadURL(candidate);
            yield break;
        }

        // Если ничего не нашли — тянем Firestore напрямую (редкий случай)
        var db = FirebaseFirestore.DefaultInstance;
        var task = db.Collection("config").Document("webview").GetSnapshotAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception == null && task.Result.Exists && task.Result.ContainsField("url"))
        {
            string url = task.Result.GetValue<string>("url");
            if (IsValidHttp(url))
            {
                baseUrl = url;
                baseDomain = ExtractDomain(baseUrl);
                PlayerPrefs.SetString(START_URL_KEY, baseUrl);
                PlayerPrefs.Save();
                webView.LoadURL(baseUrl);
                yield break;
            }
        }

        Debug.LogError("WebViewManager: Не найден валидный URL ни в кешe, ни в Firestore");
    }

    private void OnLoaded(string url)
    {
        Debug.Log("WV Loaded: " + url);

        if (string.IsNullOrEmpty(initialLoadedUrl)) initialLoadedUrl = url;
        else if (!string.IsNullOrEmpty(url) && url != initialLoadedUrl) userHasInteracted = true;

        if (IsValidHttp(url) && IsSameDomain(url, baseDomain) && !LooksLikeAuth(url))
        {
            PlayerPrefs.SetString(LAST_URL_KEY, url);
            PlayerPrefs.Save();
        }

        if (showAfterFirstLoad)
            webView.SetVisibility(true);

        // JS-трекер истории
        webView.EvaluateJS(@"(function(){try{if(window.__unity_hist_inited)return;window.__unity_hist_inited=true;window.__unity_hist_initLen=history.length;function __u_notify(){try{window.Unity.call('hist:'+history.length+':'+window.__unity_hist_initLen);}catch(e){}}var _p=history.pushState,_r=history.replaceState;history.pushState=function(){_p.apply(this,arguments);__u_notify();};history.replaceState=function(){_r.apply(this,arguments);__u_notify();};window.addEventListener('popstate',__u_notify);setTimeout(__u_notify,0);}catch(e){}})();");
    }

    private void Update()
    {
        // Android back: назад по истории/SPA; выход — только если действий не было
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
        if (webView == null) { Application.Quit(); return; }

        bool canGoBackNative = webView.CanGoBack();
        bool canGoBackSpa    = (initialHistoryLen >= 0 && currentHistoryLen > initialHistoryLen) || userHasInteracted;

        if (canGoBackNative)
        {
            webView.GoBack();
            return;
        }

        if (canGoBackSpa)
        {
            try { webView.EvaluateJS("history.back()"); } catch { }
            return;
        }

        Application.Quit();
    }

    private void OnDestroy()
    {
        if (webView != null) Destroy(webView.gameObject);
    }

    private void TryFallback()
    {
        if (webView == null) return;
        if (IsValidHttp(baseUrl)) webView.LoadURL(baseUrl);
    }

    private void ApplySafeMargins()
    {
        int left = 0, top = 0, right = 0, bottom = 0;
        if (respectSafeArea)
        {
            var sa = Screen.safeArea;
            left = Mathf.RoundToInt(sa.xMin);
            right = Mathf.RoundToInt(Screen.width - sa.xMax);
            top = Mathf.RoundToInt(sa.yMin);
            bottom = Mathf.RoundToInt(Screen.height - sa.yMax);
        }
        webView.SetMargins(left, top, right, bottom);
    }

    // ===== Helpers =====
    private static bool IsValidHttp(string s)
    {
        return !string.IsNullOrEmpty(s) && (s.StartsWith("http://") || s.StartsWith("https://"));
    }

    private bool IsLoadCandidateValid(string s) => IsValidHttp(s);

    private static string ExtractDomain(string url)
    {
        try { return new System.Uri(url).Host; } catch { return null; }
    }

    private static bool IsSameDomain(string url, string domain)
    {
        if (string.IsNullOrEmpty(domain)) return true; // допускаем любой домен, если базовый ещё не определён
        try
        {
            var host = new System.Uri(url).Host;
            return host == domain || host.EndsWith("." + domain);
        }
        catch { return false; }
    }

    private static bool LooksLikeAuth(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        var u = url.ToLowerInvariant();
        return u.Contains("login") || u.Contains("auth") || u.Contains("signin") || u.Contains("otp") || u.Contains("callback");
    }

    private void OnJSMessage(string msg)
    {
        if (!string.IsNullOrEmpty(msg) && msg.StartsWith("hist:"))
        {
            var parts = msg.Split(':');
            if (parts.Length >= 3)
            {
                int cur, init;
                if (int.TryParse(parts[1], out cur)) currentHistoryLen = cur;
                if (int.TryParse(parts[2], out init)) initialHistoryLen = init;
            }
        }
        else if (!string.IsNullOrEmpty(msg) && msg.StartsWith("loc:"))
        {
            var href = msg.Substring(4);
            if (!string.IsNullOrEmpty(href))
            {
                if (string.IsNullOrEmpty(initialLoadedUrl)) initialLoadedUrl = href;
                else if (href != initialLoadedUrl) userHasInteracted = true;
            }
        }
    }
}

// Примечания:
// • В плагине (Android) включите куки/3rd-party и DOM Storage (CookieManager + WebSettings.setDomStorageEnabled(true)).
// • В Firestore используем документ config/webview с строковым полем "url".
// • Сцены: поместите LoaderManager в самую первую сцену. В webViewSceneName разместите пустой объект с WebViewManager.
