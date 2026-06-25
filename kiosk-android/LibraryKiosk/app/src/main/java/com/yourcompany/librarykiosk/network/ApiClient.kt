package com.yourcompany.librarykiosk.network

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

object ApiClient {

    private fun base(url: String): String = url.trim().trimEnd('/')

    // ✅ ป้องกัน Cache โดยแนบ Parameter เวลา (?t=...) ไปด้านหลัง URL เสมอ
    private fun cacheBuster(): String = "?t=${System.currentTimeMillis()}"

    suspend fun getMeta(baseUrl: String): KioskMetaDto = withContext(Dispatchers.IO) {
        val json = getJson("${base(baseUrl)}/api/meta${cacheBuster()}")
        KioskMetaDto(
            bookCount = json.optInt("bookCount", 0),
            lastUpdated = json.optString("lastUpdated", null),
            appVersion = json.optString("appVersion", null)
        )
    }

    suspend fun getBrandingMeta(baseUrl: String): BrandingMetaDto = withContext(Dispatchers.IO) {
        val json = getJson("${base(baseUrl)}/api/branding/meta${cacheBuster()}")
        BrandingMetaDto(
            hasLogo = json.optBoolean("hasLogo", false),
            hasBackground = json.optBoolean("hasBackground", false),
            updatedAt = json.optString("updatedAt", null),
            logoSha256 = json.optString("logoSha256", null),
            backgroundSha256 = json.optString("backgroundSha256", null),
        )
    }

    suspend fun downloadLogo(baseUrl: String): ByteArray = withContext(Dispatchers.IO) {
        getBytes("${base(baseUrl)}/api/branding/logo${cacheBuster()}")
    }

    suspend fun downloadBackground(baseUrl: String): ByteArray = withContext(Dispatchers.IO) {
        getBytes("${base(baseUrl)}/api/branding/background${cacheBuster()}")
    }

    suspend fun fetchAllBooks(baseUrl: String): List<BookDto> = withContext(Dispatchers.IO) {
        val url = "${base(baseUrl)}/api/books${cacheBuster()}"
        val arr = getJsonArray(url)
        parseBooks(arr)
    }

    suspend fun fetchBooks(baseUrl: String, query: String, category: String): List<BookDto> = withContext(Dispatchers.IO) {
        val q = query.trim()
        val c = category.trim()
        val sb = StringBuilder("${base(baseUrl)}/api/books${cacheBuster()}")
        sb.append("&q=").append(URLEncoder.encode(q, "UTF-8"))
        sb.append("&category=").append(URLEncoder.encode(c, "UTF-8"))
        val arr = getJsonArray(sb.toString())
        parseBooks(arr)
    }

    private fun parseBooks(arr: JSONArray): List<BookDto> {
        val out = ArrayList<BookDto>(arr.length())
        for (i in 0 until arr.length()) {
            val o = arr.optJSONObject(i) ?: continue

            val regNo = o.optString("regNo", o.optString("reg_no", ""))
            val title = o.optString("title", "")
            val category = o.optString("category", "")
            val publisher = o.optString("publisher", "")
            val shelf = o.optString("shelf", "")

            out.add(BookDto(regNo = regNo, title = title, category = category, publisher = publisher, shelf = shelf))
        }
        return out
    }

    private fun getJson(url: String): JSONObject {
        val body = getText(url)
        return JSONObject(body)
    }

    private fun getJsonArray(url: String): JSONArray {
        val body = getText(url)
        return JSONArray(body)
    }

    private fun getText(url: String): String {
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = 12_000
            readTimeout = 12_000
            useCaches = false // ✅ สั่งห้าม Android ใช้หน่วยความจำ (Cache) เด็ดขาด
        }

        val code = conn.responseCode
        val stream = if (code in 200..299) conn.inputStream else conn.errorStream
        val text = stream?.use { it.readBytes().toString(Charsets.UTF_8) }.orEmpty()

        if (code !in 200..299) {
            throw RuntimeException("HTTP $code @ $url\n$text")
        }
        return text
    }

    private fun getBytes(url: String): ByteArray {
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = 12_000
            readTimeout = 12_000
            useCaches = false // ✅ สั่งห้าม Android ใช้หน่วยความจำ (Cache) เด็ดขาด
        }

        val code = conn.responseCode
        val stream = if (code in 200..299) conn.inputStream else conn.errorStream
        val bytes = stream?.use { readAllBytes(it) } ?: ByteArray(0)

        if (code !in 200..299) {
            val msg = bytes.toString(Charsets.UTF_8)
            throw RuntimeException("HTTP $code @ $url\n$msg")
        }
        return bytes
    }

    private fun readAllBytes(input: InputStream): ByteArray {
        val bos = ByteArrayOutputStream()
        val buf = ByteArray(8192)
        while (true) {
            val n = input.read(buf)
            if (n <= 0) break
            bos.write(buf, 0, n)
        }
        return bos.toByteArray()
    }
}