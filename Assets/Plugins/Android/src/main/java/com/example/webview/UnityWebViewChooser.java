package com.example.webview;

import android.app.Activity;
import android.app.DownloadManager;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.ActivityNotFoundException;
import android.content.BroadcastReceiver;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.provider.MediaStore;
import android.provider.Settings;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.CookieManager;
import android.webkit.DownloadListener;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;

import androidx.core.content.FileProvider;

import java.io.File;
import java.net.URLDecoder;
import java.util.ArrayList;
import java.util.Locale;

public class UnityWebViewChooser {

    private static final String FRAGMENT_TAG = "UnityChooserFragment";
    private static ValueCallback<Uri[]> pendingCallback;
    private static Uri cameraOutputUri;
    private static long lastDownloadId = -1L;
    private static BroadcastReceiver downloadReceiver;

    public static void hook(final Activity activity) {
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                WebView wv = findWebViewRecursive(activity.getWindow().getDecorView());
                if (wv == null) return;

                WebSettings s = wv.getSettings();
                s.setJavaScriptEnabled(true);
                s.setDomStorageEnabled(true);
                s.setAllowFileAccess(true);
                s.setAllowContentAccess(true);
                s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);

                final ChooserFragment chooser = ensureChooserFragment(activity);
                wv.setWebChromeClient(new WebChromeClient() {
                    @Override
                    public boolean onShowFileChooser(WebView webView, ValueCallback<Uri[]> filePathCallback, FileChooserParams fileChooserParams) {
                        // cancel previous
                        if (pendingCallback != null) pendingCallback.onReceiveValue(null);
                        pendingCallback = filePathCallback;
                        chooser.openChooser(fileChooserParams);
                        return true;
                    }
                });

                // Attach DownloadManager listener for APK (and other) downloads
                wv.setDownloadListener(new DownloadListener() {
                    @Override
                    public void onDownloadStart(String url, String userAgent, String contentDisposition, String mimeType, long contentLength) {
                        startDownload(activity, url, userAgent, contentDisposition, mimeType);
                    }
                });

                if (downloadReceiver == null) {
                    downloadReceiver = new BroadcastReceiver() {
                        @Override public void onReceive(Context context, Intent intent) {
                            if (!DownloadManager.ACTION_DOWNLOAD_COMPLETE.equals(intent.getAction())) return;
                            long id = intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1);
                            if (id != lastDownloadId) return;
                            DownloadManager dm = (DownloadManager) context.getSystemService(Context.DOWNLOAD_SERVICE);
                            if (dm == null) return;
                            Uri fileUri = dm.getUriForDownloadedFile(id);
                            if (fileUri != null) promptInstall(activity, fileUri);
                        }
                    };
                    activity.registerReceiver(downloadReceiver, new IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE));
                }
            }
        });
    }

    private static ChooserFragment ensureChooserFragment(Activity act) {
        FragmentManager fm = act.getFragmentManager();
        Fragment f = fm.findFragmentByTag(FRAGMENT_TAG);
        if (f instanceof ChooserFragment) return (ChooserFragment) f;
        ChooserFragment nf = new ChooserFragment();
        fm.beginTransaction().add(nf, FRAGMENT_TAG).commitAllowingStateLoss();
        fm.executePendingTransactions();
        return nf;
    }

    public static class ChooserFragment extends Fragment {
        private static final int REQ_CHOOSE = 9876;

        @Override public void onActivityResult(int requestCode, int resultCode, Intent data) {
            super.onActivityResult(requestCode, resultCode, data);
            if (requestCode != REQ_CHOOSE) return;
            ValueCallback<Uri[]> cb = pendingCallback;
            pendingCallback = null;
            ArrayList<Uri> result = new ArrayList<>();
            if (resultCode == Activity.RESULT_OK) {
                if (data != null) {
                    Uri d = data.getData();
                    if (d != null) result.add(d);
                    ClipData cd = data.getClipData();
                    if (cd != null) {
                        for (int i = 0; i < cd.getItemCount(); i++) {
                            Uri u = cd.getItemAt(i).getUri();
                            if (u != null) result.add(u);
                        }
                    }
                }
                if (cameraOutputUri != null) {
                    result.add(cameraOutputUri);
                }
            }
            cameraOutputUri = null;
            if (cb != null) cb.onReceiveValue(result.isEmpty() ? null : result.toArray(new Uri[0]));
        }

        void openChooser(WebChromeClient.FileChooserParams params) {
            Activity act = getActivity();
            if (act == null) return;

            String[] accept = params != null ? params.getAcceptTypes() : new String[0];
            boolean allowMultiple = params != null && params.getMode() == WebChromeClient.FileChooserParams.MODE_OPEN_MULTIPLE;
            String primary = (accept != null && accept.length == 1 && accept[0] != null && !accept[0].isEmpty()) ? accept[0] : "*/*";

            Intent open = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            open.addCategory(Intent.CATEGORY_OPENABLE);
            open.setType(primary);
            if (accept != null && accept.length > 1) {
                open.putExtra(Intent.EXTRA_MIME_TYPES, accept);
            }
            open.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, allowMultiple);

            Intent camera = createCameraIntent(act);

            Intent chooser = new Intent(Intent.ACTION_CHOOSER);
            chooser.putExtra(Intent.EXTRA_INTENT, open);
            if (camera != null) chooser.putExtra(Intent.EXTRA_INITIAL_INTENTS, new Intent[]{camera});
            try {
                startActivityForResult(chooser, REQ_CHOOSE);
            } catch (ActivityNotFoundException e) {
                // fallback: open only document picker
                try {
                    startActivityForResult(open, REQ_CHOOSE);
                } catch (Exception ignored) {
                    if (pendingCallback != null) { pendingCallback.onReceiveValue(null); pendingCallback = null; }
                }
            }
        }

        private Intent createCameraIntent(Context ctx) {
            try {
                File photo = File.createTempFile("uwv_photo_", ".jpg", ctx.getExternalCacheDir());
                Uri uri = FileProvider.getUriForFile(ctx, ctx.getPackageName() + ".fileprovider", photo);
                cameraOutputUri = uri;
                Intent i = new Intent(MediaStore.ACTION_IMAGE_CAPTURE);
                i.putExtra(MediaStore.EXTRA_OUTPUT, uri);
                i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
                return i;
            } catch (Exception e) {
                cameraOutputUri = null;
                return null;
            }
        }
    }

    private static WebView findWebViewRecursive(View v) {
        if (v instanceof WebView) return (WebView) v;
        if (v instanceof ViewGroup) {
            ViewGroup g = (ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) {
                WebView w = findWebViewRecursive(g.getChildAt(i));
                if (w != null) return w;
            }
        }
        return null;
    }

    private static void startDownload(Activity activity, String url, String userAgent, String contentDisposition, String mimeType) {
        if (url == null || url.isEmpty()) return;
        String finalMime = resolveMime(mimeType, url);
        String fileName = resolveFileName(url, contentDisposition, finalMime);
        DownloadManager dm = (DownloadManager) activity.getSystemService(Context.DOWNLOAD_SERVICE);
        if (dm == null) return;
        DownloadManager.Request req = new DownloadManager.Request(Uri.parse(url));
        req.setMimeType(finalMime);
        req.setTitle(fileName);
        req.setDescription("Загрузка " + fileName);
        req.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
        req.setAllowedOverRoaming(true);
        req.setAllowedNetworkTypes(DownloadManager.Request.NETWORK_WIFI | DownloadManager.Request.NETWORK_MOBILE);
        String cookie = CookieManager.getInstance().getCookie(url);
        if (cookie != null) req.addRequestHeader("Cookie", cookie);
        if (userAgent != null) req.addRequestHeader("User-Agent", userAgent);
        req.setDestinationInExternalFilesDir(activity, Environment.DIRECTORY_DOWNLOADS, fileName);
        lastDownloadId = dm.enqueue(req);
    }

    private static String resolveMime(String mime, String url) {
        if (mime != null && !mime.isEmpty() && !"application/octet-stream".equals(mime)) return mime;
        return url.toLowerCase(Locale.US).endsWith(".apk") ? "application/vnd.android.package-archive" : "application/octet-stream";
    }

    private static String resolveFileName(String url, String cd, String mime) {
        try {
            if (cd != null) {
                java.util.regex.Matcher m = java.util.regex.Pattern.compile("filename\\*?=\"?([^\";]+)\"?", java.util.regex.Pattern.CASE_INSENSITIVE).matcher(cd);
                if (m.find()) return safeName(URLDecoder.decode(m.group(1), "UTF-8"));
            }
        } catch (Throwable ignored) {}
        String base = Uri.parse(url).getLastPathSegment();
        if (base == null || base.isEmpty()) base = "download";
        String name = safeName(base);
        if (name.toLowerCase(Locale.US).endsWith(".apk")) return name;
        return "application/vnd.android.package-archive".equals(mime) ? name + ".apk" : name;
    }

    private static String safeName(String n) {
        return n.replaceAll("[\\\\/:*?\"<>|]", "_").substring(0, Math.min(64, n.length()));
    }

    private static void promptInstall(Activity activity, Uri fileUri) {
        Intent install = new Intent(Intent.ACTION_INSTALL_PACKAGE);
        install.setData(fileUri);
        install.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        install.putExtra(Intent.EXTRA_NOT_UNKNOWN_SOURCE, true);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && !activity.getPackageManager().canRequestPackageInstalls()) {
            Intent s = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES);
            s.setData(Uri.parse("package:" + activity.getPackageName()));
            s.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(s);
            return;
        }
        try {
            activity.startActivity(install);
        } catch (Throwable t) {
            try {
                // Fallback through FileProvider
                File alt = new File(activity.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), fileUri.getLastPathSegment());
                if (alt.exists()) {
                    Uri fp = FileProvider.getUriForFile(activity, activity.getPackageName() + ".fileprovider", alt);
                    Intent i2 = new Intent(Intent.ACTION_INSTALL_PACKAGE);
                    i2.setData(fp);
                    i2.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
                    activity.startActivity(i2);
                }
            } catch (Throwable ignored) {}
        }
    }
}


