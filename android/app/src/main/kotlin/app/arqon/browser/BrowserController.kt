package app.arqon.browser

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Bitmap
import android.webkit.CookieManager
import android.webkit.DownloadListener
import android.webkit.URLUtil
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.webkit.UserAgentMetadata
import androidx.webkit.WebSettingsCompat
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import app.arqon.ArqonApp
import app.arqon.core.input.InputKind
import app.arqon.core.input.UserInput
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

/**
 * The built-in browser. In "desktop site" mode it introduces itself as Chrome on Windows (name, version, platform, and the
 * "is this a phone" hints), so websites that hide download links from phones show the full page. Whatever the page offers for
 * download (a file, a .torrent, a magnet link) goes to Arqon, which fetches it over every connection.
 */
class BrowserController(context: Context, private val app: ArqonApp, private val say: (String) -> Unit) {
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

    @SuppressLint("SetJavaScriptEnabled")
    val webView: WebView = WebView(context).apply {
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
                return false
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

    private var scriptAdded = false

    fun load(typed: String) {
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
