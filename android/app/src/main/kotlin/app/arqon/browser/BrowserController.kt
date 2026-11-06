package app.arqon.browser

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Bitmap
import android.webkit.CookieManager
import android.webkit.DownloadListener
import android.webkit.URLUtil
import android.content.Intent
import android.net.Uri
import android.view.ViewGroup
import android.webkit.RenderProcessGoneDetail
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.webkit.UserAgentMetadata
import androidx.webkit.WebSettingsCompat
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import app.arqon.ArqonApp
import app.arqon.R
import app.arqon.stats.Features
import app.arqon.core.input.InputKind
import app.arqon.core.input.UserInput
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

/**
 * The built-in browser. In "desktop site" mode it introduces itself as Chrome on Windows (name, version, platform, and the
 * "is this a phone" hints), so websites that hide download links from phones show the full page. Whatever the page offers for
 * download (a file, a .torrent, a magnet link) goes to Arqon, which fetches it over every connection.
 */
class BrowserController(private val context: Context, private val app: ArqonApp, private val say: (String) -> Unit) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    private val _progress = MutableStateFlow(100)
    val progress: StateFlow<Int> = _progress
    private val _url = MutableStateFlow("")
    val url: StateFlow<String> = _url
    private val _canGoBack = MutableStateFlow(false)
    val canGoBack: StateFlow<Boolean> = _canGoBack
    private val _canGoForward = MutableStateFlow(false)
    val canGoForward: StateFlow<Boolean> = _canGoForward

    private val mobileAgent: String = WebSettings.getDefaultUserAgent(context)
    private var desktop = app.settings.desktopMode.value

    /** The page view. It is replaced by a new one when Android's web engine process dies (see [recover]); the screen follows. */
    var webView: WebView by mutableStateOf(createWebView())
        private set

    private var destroyed = false
    private var applied: Boolean? = null
    private var scriptAdded = false

    @SuppressLint("SetJavaScriptEnabled")
    private fun createWebView(): WebView = WebView(context).apply {
        settings.javaScriptEnabled = true
        settings.domStorageEnabled = true
        settings.setSupportZoom(true)
        settings.builtInZoomControls = true
        settings.displayZoomControls = false
        settings.mediaPlaybackRequiresUserGesture = false
        settings.mixedContentMode = WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE
        settings.setSupportMultipleWindows(false)
        CookieManager.getInstance().setAcceptCookie(true)
        CookieManager.getInstance().setAcceptThirdPartyCookies(this, true)

        webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val target = request.url.toString()
                if (target.startsWith("magnet:", ignoreCase = true)) {
                    handOver(target)
                    return true
                }
                val scheme = request.url.scheme?.lowercase()
                if (scheme == null || scheme in WEB_SCHEMES) return false
                // tel:, mailto:, market:, intent: ... the page view would show an error page for them
                openOutside(target)
                return true
            }

            override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail): Boolean {
                // without this the whole app is killed together with the web engine (low memory phones do it often)
                recover(view)
                return true
            }

            override fun onPageStarted(view: WebView, url: String, favicon: Bitmap?) {
                _url.value = url
                refreshNavigation(view)
            }

            override fun onPageFinished(view: WebView, url: String) {
                _url.value = url
                refreshNavigation(view)
            }
        }
        webChromeClient = object : WebChromeClient() {
            override fun onProgressChanged(view: WebView, newProgress: Int) {
                _progress.value = newProgress
            }
        }
        setDownloadListener(DownloadListener { url, userAgent, contentDisposition, mimeType, _ ->
            onDownload(url, userAgent, contentDisposition, mimeType)
        })
    }

    private fun refreshNavigation(view: WebView) {
        _canGoBack.value = view.canGoBack()
        _canGoForward.value = view.canGoForward()
    }

    /** A magnet link or a .torrent link found on a page. */
    private fun handOver(text: String, headers: Map<String, String> = emptyMap()) {
        scope.launch {
            val error = app.downloads.addText(text, headers)
            say(error ?: "OK")
        }
    }

    private fun onDownload(url: String, userAgent: String?, contentDisposition: String?, mimeType: String?) {
        val headers = HashMap<String, String>()
        CookieManager.getInstance().getCookie(url)?.let { headers["Cookie"] = it }
        userAgent?.let { headers["User-Agent"] = it }
        webView.url?.let { headers["Referer"] = it }

        if (!url.startsWith("http", ignoreCase = true)) {
            // blob: and data: links only exist inside the page; Arqon cannot fetch them
            say(context.getString(R.string.err_bad_link))
            return
        }
        val isTorrent = mimeType == "application/x-bittorrent" || UserInput.classify(url) == InputKind.TORRENT_URL
        if (isTorrent) {
            handOver(url, headers)
            return
        }
        val name = URLUtil.guessFileName(url, contentDisposition, mimeType)
        app.downloads.addHttp(url, headers, name)
        say(name)
    }

    /** Switches between the phone and the computer version of websites and reloads the page. */
    fun applyMode(wantDesktop: Boolean, reload: Boolean = true) {
        // the screen asks again every time the tab is shown: nothing to do (and no reload) when the mode is the same
        if (applied == wantDesktop) return
        applied = wantDesktop
        desktop = wantDesktop
        val s = webView.settings
        if (wantDesktop) {
            s.userAgentString = DESKTOP_AGENT
            s.useWideViewPort = true
            s.loadWithOverviewMode = true
            if (WebViewFeature.isFeatureSupported(WebViewFeature.USER_AGENT_METADATA)) {
                val brands = listOf(
                    UserAgentMetadata.BrandVersion.Builder().setBrand("Chromium").setMajorVersion("136").setFullVersion("136.0.7103.113").build(),
                    UserAgentMetadata.BrandVersion.Builder().setBrand("Google Chrome").setMajorVersion("136").setFullVersion("136.0.7103.113").build(),
                    UserAgentMetadata.BrandVersion.Builder().setBrand("Not.A/Brand").setMajorVersion("99").setFullVersion("99.0.0.0").build(),
                )
                val metadata = UserAgentMetadata.Builder()
                    .setBrandVersionList(brands).setFullVersion("136.0.7103.113").setPlatform("Windows").setPlatformVersion("10.0.0")
                    .setArchitecture("x86").setBitness(64).setModel("").setMobile(false).build()
                try { WebSettingsCompat.setUserAgentMetadata(s, metadata) } catch (_: Exception) { }
            }
            if (WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT) && !scriptAdded) {
                // A computer has no touch screen: pages that test for one to decide "this is a phone" see what they expect.
                try {
                    WebViewCompat.addDocumentStartJavaScript(webView, DESKTOP_SCRIPT, setOf("*"))
                    scriptAdded = true
                } catch (_: Exception) { }
            }
        } else {
            s.userAgentString = mobileAgent
            s.useWideViewPort = false
            s.loadWithOverviewMode = false
            if (WebViewFeature.isFeatureSupported(WebViewFeature.USER_AGENT_METADATA)) {
                try { WebSettingsCompat.setUserAgentMetadata(s, UserAgentMetadata.Builder().setMobile(true).build()) } catch (_: Exception) { }
            }
        }
        if (reload && webView.url != null) webView.reload()
    }

    private fun recover(old: WebView) {
        if (destroyed || old !== webView) return
        val last = _url.value
        (old.parent as? ViewGroup)?.removeView(old)
        try { old.destroy() } catch (_: Exception) { }
        scriptAdded = false
        applied = null
        webView = createWebView()
        applyMode(desktop, reload = false)
        _progress.value = 100
        if (last.startsWith("http", ignoreCase = true)) webView.loadUrl(last)
    }

    private fun openOutside(target: String) {
        try {
            val intent = if (target.startsWith("intent:", ignoreCase = true)) Intent.parseUri(target, Intent.URI_INTENT_SCHEME)
            else Intent(Intent.ACTION_VIEW, Uri.parse(target))
            // a page must not pick the app's inner parts
            intent.addCategory(Intent.CATEGORY_BROWSABLE)
            intent.component = null
            intent.selector = null
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            context.startActivity(intent)
        } catch (_: Exception) {
            // no app for it: the page stays as it is
        }
    }

    /** Called when the screen is gone for good: the page view must not keep running (sound, scripts) with no one to see it. */
    fun destroy() {
        destroyed = true
        scope.cancel()
        val view = webView
        (view.parent as? ViewGroup)?.removeView(view)
        try { view.stopLoading(); view.destroy() } catch (_: Exception) { }
    }

    fun load(typed: String) {
        Features.use("browser_page")
        val address = UserInput.addressFor(typed)
        if (address.startsWith("magnet:", ignoreCase = true)) handOver(address) else webView.loadUrl(address)
    }

    fun goBack() { if (webView.canGoBack()) webView.goBack() }

    fun goForward() { if (webView.canGoForward()) webView.goForward() }

    fun reload() = webView.reload()

    fun stop() = webView.stopLoading()

    init {
        applyMode(desktop, reload = false)
        webView.loadUrl("https://duckduckgo.com/")
    }

    companion object {
        private val WEB_SCHEMES = setOf("http", "https", "about", "data", "blob", "file", "javascript")

        const val DESKTOP_AGENT =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36"

        private const val DESKTOP_SCRIPT = """
            (function () {
              try { Object.defineProperty(navigator, 'maxTouchPoints', { get: function () { return 0; } }); } catch (e) {}
              try { delete window.ontouchstart; } catch (e) {}
            })();
        """
    }
}
