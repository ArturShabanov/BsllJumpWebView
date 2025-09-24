package com.example.webview;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;

import androidx.core.content.FileProvider;

import java.io.File;
import java.util.ArrayList;

public class UnityWebViewChooser {

    private static final String FRAGMENT_TAG = "UnityChooserFragment";
    private static ValueCallback<Uri[]> pendingCallback;
    private static Uri cameraOutputUri;

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
}


