using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Firebase.Firestore;

namespace App.Boot
{
    public class BootLoader : MonoBehaviour
    {
        [Header("Scene Names")]
        [Tooltip("Сцена, где расположен WebViewManager")] public string webViewSceneName = "WebViewScene";
        [Tooltip("Фолбэк сцена игры, если URL не найден")] public string fallbackGameSceneName = "Game";

        [Header("Behavior")]
        [Tooltip("Сначала пробуем кешированный START_URL, чтобы открыть WebView максимально рано")]
        public bool tryCachedFirst = true;
        [Tooltip("Таймаут ожидания Firestore (Server), сек")]
        public float firestoreTimeoutSeconds = 3f;

        [Header("Safety/Debug")]
        [Tooltip("НЕ трогать FPS/всинк игры (рекомендовано)")]
        public bool setTargetFrameRate = false;
        public int targetFps = 60;
        [Tooltip("Разрешить http-ссылку (ТОЛЬКО для отладки — куки SameSite=None работать не будут)")]
        public bool allowHttpForDebug = false;

        private const string START_URL_KEY        = "webview_start_url"; // базовый URL из Firestore
        private const string WEBVIEW_UNLOCKED_KEY = "webview_unlocked";  // ставится при первой успешной загрузке WebView

        private bool _sceneChosen;

        private void Start()
        {
            if (setTargetFrameRate) Application.targetFrameRate = targetFps;
            StartCoroutine(Bootstrap());
        }

        private IEnumerator Bootstrap()
        {
            // 0) Липкий режим: если веб уже когда-то открывался — всегда идём в WebView
            if (PlayerPrefs.GetInt(WEBVIEW_UNLOCKED_KEY, 0) == 1)
            {
                LoadSceneOnce(webViewSceneName);
                StartCoroutine(RefreshStartUrlSilently()); // тихо освежим ссылку
                yield break;
            }

            // 1) (опц.) быстрый старт из кеша до Firestore
            var cached = PlayerPrefs.GetString(START_URL_KEY, string.Empty);
            if (tryCachedFirst && IsValidStartUrl(cached))
            {
                LoadSceneOnce(webViewSceneName);
                StartCoroutine(RefreshStartUrlSilently());
                yield break;
            }

            // 2) Firestore: links/sweetbonanza.samsung
            var db     = FirebaseFirestore.DefaultInstance;
            var docRef = db.Collection("links").Document("sweetbonanza");

            bool ok = false;

            // 2a) СНАЧАЛА пробуем Cache (моментально, если уже когда-то тянули)
            var cacheTask = docRef.GetSnapshotAsync(Source.Cache);
            yield return new WaitUntil(() => cacheTask.IsCompleted);
            if (cacheTask.Exception == null && cacheTask.Result != null && cacheTask.Result.Exists &&
                cacheTask.Result.ContainsField("samsung"))
            {
                var cachedUrl = cacheTask.Result.GetValue<string>("samsung");
                if (IsValidStartUrl(cachedUrl))
                {
                    PlayerPrefs.SetString(START_URL_KEY, cachedUrl);
                    PlayerPrefs.Save();
                    ok = true;
                }
            }

            if (ok)
            {
                LoadSceneOnce(webViewSceneName);
                StartCoroutine(RefreshStartUrlSilently()); // всё равно освежим с сервера
                yield break;
            }

            // 2b) Потом пробуем Server (с таймаутом)
            var netTask = docRef.GetSnapshotAsync(Source.Server);
            float t = 0f;
            while (!netTask.IsCompleted && t < firestoreTimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (netTask.IsCompleted && netTask.Exception == null && netTask.Result != null &&
                netTask.Result.Exists && netTask.Result.ContainsField("samsung"))
            {
                var freshUrl = netTask.Result.GetValue<string>("samsung");
                if (IsValidStartUrl(freshUrl))
                {
                    PlayerPrefs.SetString(START_URL_KEY, freshUrl);
                    PlayerPrefs.Save();
                    ok = true;
                }
            }

            // 3) решаем, какую сцену грузить
            if (ok || IsValidStartUrl(PlayerPrefs.GetString(START_URL_KEY, string.Empty)))
                LoadSceneOnce(webViewSceneName);
            else
                LoadSceneOnce(fallbackGameSceneName);
        }

        private IEnumerator RefreshStartUrlSilently()
        {
            var db     = FirebaseFirestore.DefaultInstance;
            var docRef = db.Collection("links").Document("sweetbonanza");
            var task   = docRef.GetSnapshotAsync(Source.Server);
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Exception == null && task.Result != null && task.Result.Exists && task.Result.ContainsField("samsung"))
            {
                var url = task.Result.GetValue<string>("samsung");
                if (IsValidStartUrl(url))
                {
                    PlayerPrefs.SetString(START_URL_KEY, url);
                    PlayerPrefs.Save();
                }
            }
        }

        // ---- helpers ----
        private void LoadSceneOnce(string name)
        {
            if (_sceneChosen) return;
            _sceneChosen = true;
            SceneManager.LoadScene(name, LoadSceneMode.Single);
        }

        private bool IsValidStartUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.StartsWith("https://")) return true;
            if (allowHttpForDebug && s.StartsWith("http://")) return true; // DEBUG ONLY
            return false;
        }
    }
}
