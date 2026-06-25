package com.yourcompany.librarykiosk.ui

import android.app.Application
import android.content.Context
import android.util.Log
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.HubConnectionState
import com.yourcompany.librarykiosk.data.BookEntity
import com.yourcompany.librarykiosk.network.ApiClient
import com.yourcompany.librarykiosk.security.PinSecurity
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.io.File

class MainViewModel(app: Application) : AndroidViewModel(app) {

    private val prefs = app.getSharedPreferences("kiosk_prefs", Context.MODE_PRIVATE)

    private val _uiState = MutableStateFlow(
        UiState(
            serverBaseUrl = prefs.getString(KEY_SERVER_URL, DEFAULT_URL) ?: DEFAULT_URL,
            serverBaseUrlInput = prefs.getString(KEY_SERVER_URL, DEFAULT_URL) ?: DEFAULT_URL,
            displayName = prefs.getString(KEY_DISPLAY_NAME, "Library Kiosk") ?: "Library Kiosk",
            displayNameInput = prefs.getString(KEY_DISPLAY_NAME, "Library Kiosk") ?: "Library Kiosk",
            // 🔥 เริ่มต้นเป็น URL ถ้ามีค่า
            logoImagePath = null,
            backgroundImagePath = null
        )
    )
    val uiState: StateFlow<UiState> = _uiState

    private var allBooks: List<BookEntity> = emptyList()

    // ตัวแปรสำหรับ SignalR
    private var hubConnection: HubConnection? = null

    fun initLoad() {
        // ✅ ป้องกันปัญหาแอปเด้ง: ย้ายการโหลดไปที่ IO Thread
        viewModelScope.launch(Dispatchers.IO) {
            syncNow()
            startSignalR() // เริ่มการเชื่อมต่อ WebSockets
        }
    }

    // ---------------- SignalR (Real-time Sync) ----------------

    private fun startSignalR() {
        val baseUrl = _uiState.value.serverBaseUrl
        if (baseUrl.isBlank()) return

        viewModelScope.launch(Dispatchers.IO) {
            try {
                // หยุดอันเก่าก่อน (ถ้ามี)
                if (hubConnection?.connectionState == HubConnectionState.CONNECTED) {
                    hubConnection?.stop()?.blockingAwait()
                }

                // สร้าง Connection ไปที่ /hubs/library
                val hubUrl = "$baseUrl/hubs/library"
                hubConnection = HubConnectionBuilder.create(hubUrl).build()

                // ดักรอฟังคำสั่ง "SyncRequested" จาก Server
                hubConnection?.on("SyncRequested", {
                    Log.d("SignalR", "ได้รับคำสั่ง Sync จาก Server!")
                    syncNow() // อัปเดตข้อมูลทันที
                })

                // สร้างระบบต่อเน็ตใหม่เองเมื่อสายหลุด
                hubConnection?.onClosed { exception ->
                    Log.e("SignalR", "การเชื่อมต่อหลุด: ${exception?.message}. กำลังพยายามเชื่อมต่อใหม่ใน 5 วินาที...")
                    viewModelScope.launch(Dispatchers.IO) {
                        delay(5000) // หน่วงเวลา 5 วินาทีป้องกันการโหลดหนักเกินไป
                        startSignalR() // เรียกตัวเองเพื่อต่อใหม่
                    }
                }

                // เริ่มการเชื่อมต่อ
                hubConnection?.start()?.blockingAwait()
                Log.d("SignalR", "เชื่อมต่อ WebSockets สำเร็จ")

            } catch (e: Exception) {
                Log.e("SignalR", "เชื่อมต่อล้มเหลว: ${e.message}")
                // ถ้าตอนเปิดแอปครั้งแรกเชื่อมต่อไม่ติด (เช่น Server ยังไม่เปิด) ให้ลองใหม่ใน 5 วินาที
                viewModelScope.launch(Dispatchers.IO) {
                    delay(5000)
                    startSignalR()
                }
            }
        }
    }

    // ---------------- UI actions ----------------

    fun setQuery(q: String) {
        _uiState.update { it.copy(query = q) }
        applyFilter()
    }

    fun clearQuery() {
        _uiState.update { it.copy(query = "") }
        applyFilter()
    }

    fun setCategory(c: String) {
        _uiState.update { it.copy(category = c) }
        applyFilter()
    }

    fun selectBook(b: BookEntity) {
        _uiState.update { it.copy(selectedBook = b, coverNonce = System.currentTimeMillis()) }
    }

    fun clearSelectedBook() {
        _uiState.update { it.copy(selectedBook = null) }
    }

    fun clearMessage() {
        _uiState.update { it.copy(message = null) }
    }

    // ---------------- PIN flow ----------------

    fun openPinDialog() {
        refreshPinState()
        _uiState.update { it.copy(showPinDialog = true, pinVerify = it.pinVerify.copy(input = "", error = null)) }
    }

    fun closePinDialog() {
        _uiState.update { it.copy(showPinDialog = false) }
    }

    fun onPinInputChanged(v: String) {
        _uiState.update { it.copy(pinVerify = it.pinVerify.copy(input = v, error = null)) }
    }

    // ✅ ป้องกันแอปเด้ง: ย้ายการอ่าน Database ของ PIN ไปที่ IO Thread
    fun verifyAdminPin() {
        refreshPinState()
        val st = _uiState.value.pinVerify
        if (st.isLocked) return

        val input = st.input.trim()
        if (input.length < 4) {
            _uiState.update { it.copy(pinVerify = it.pinVerify.copy(error = "PIN ไม่ถูกต้อง")) }
            return
        }

        viewModelScope.launch(Dispatchers.IO) {
            val savedSalt = prefs.getString(KEY_PIN_SALT, null)
            val savedHash = prefs.getString(KEY_PIN_HASH, null)
            val savedIter = prefs.getInt(KEY_PIN_ITER, 0)

            val ok = if (savedSalt.isNullOrBlank() || savedHash.isNullOrBlank() || savedIter <= 0) {
                input == "1234"
            } else {
                PinSecurity.verifyPin(input, savedSalt, savedHash, savedIter)
            }

            if (ok) {
                resetPinLock()
                _uiState.update {
                    it.copy(showPinDialog = false, showSettingsDialog = true, pinVerify = it.pinVerify.copy(input = "", error = null))
                }
            } else {
                val remain = (prefs.getInt(KEY_PIN_FAIL_COUNT, 0) + 1).coerceAtMost(MAX_ATTEMPTS)
                prefs.edit().putInt(KEY_PIN_FAIL_COUNT, remain).apply()

                val attemptsLeft = (MAX_ATTEMPTS - remain).coerceAtLeast(0)
                if (attemptsLeft <= 0) {
                    val until = (System.currentTimeMillis() / 1000L) + LOCK_SECONDS
                    prefs.edit().putLong(KEY_PIN_LOCK_UNTIL, until).apply()
                }
                refreshPinState()
                _uiState.update { it.copy(pinVerify = it.pinVerify.copy(error = "PIN ไม่ถูกต้อง")) }
            }
        }
    }

    // ---------------- Settings dialog ----------------

    fun closeSettingsDialog() {
        _uiState.update { it.copy(showSettingsDialog = false) }
    }

    fun setServerBaseUrlInput(v: String) {
        _uiState.update { it.copy(serverBaseUrlInput = v) }
    }

    fun saveServerBaseUrl() {
        val url = _uiState.value.serverBaseUrlInput.trim().trimEnd('/')
        if (url.isBlank()) {
            _uiState.update { it.copy(message = "URL ว่างไม่ได้") }
            return
        }
        prefs.edit().putString(KEY_SERVER_URL, url).apply()
        _uiState.update { it.copy(serverBaseUrl = url, message = "บันทึก URL แล้ว") }

        syncNow()
        startSignalR() // เชื่อมต่อ WebSockets ไปยัง URL ใหม่
    }

    fun setDisplayNameInput(v: String) {
        _uiState.update { it.copy(displayNameInput = v) }
    }

    fun saveDisplayNameToServer() {
        val name = _uiState.value.displayNameInput.trim()
        if (name.isBlank()) {
            _uiState.update { it.copy(message = "ชื่อว่างไม่ได้") }
            return
        }
        prefs.edit().putString(KEY_DISPLAY_NAME, name).apply()
        _uiState.update { it.copy(displayName = name, message = "บันทึกชื่อแล้ว") }
    }

    fun openChangePinDialog() {
        _uiState.update { it.copy(showChangePinDialog = true, pinChange = PinChangeState()) }
    }

    fun closeChangePinDialog() {
        _uiState.update { it.copy(showChangePinDialog = false) }
    }

    fun onChangeCurrentPin(v: String) {
        _uiState.update { it.copy(pinChange = it.pinChange.copy(currentPin = v, error = null, success = null)) }
    }

    fun onChangeNewPin(v: String) {
        _uiState.update { it.copy(pinChange = it.pinChange.copy(newPin = v, error = null, success = null)) }
    }

    fun onChangeConfirmPin(v: String) {
        _uiState.update { it.copy(pinChange = it.pinChange.copy(confirmPin = v, error = null, success = null)) }
    }

    fun submitChangePin() {
        val f = _uiState.value.pinChange
        val cur = f.currentPin.trim()
        val nw = f.newPin.trim()
        val cf = f.confirmPin.trim()

        if (nw.length !in 4..8) {
            _uiState.update { it.copy(pinChange = it.pinChange.copy(error = "PIN ใหม่ต้อง 4-8 หลัก")) }
            return
        }
        if (nw != cf) {
            _uiState.update { it.copy(pinChange = it.pinChange.copy(error = "ยืนยัน PIN ไม่ตรงกัน")) }
            return
        }

        // ✅ ป้องกันแอปเด้ง: ย้ายการอ่าน Database ไปที่ IO Thread
        viewModelScope.launch(Dispatchers.IO) {
            val savedSalt = prefs.getString(KEY_PIN_SALT, null)
            val savedHash = prefs.getString(KEY_PIN_HASH, null)
            val savedIter = prefs.getInt(KEY_PIN_ITER, 0)

            val ok = if (savedSalt.isNullOrBlank() || savedHash.isNullOrBlank() || savedIter <= 0) {
                cur == "1234"
            } else {
                PinSecurity.verifyPin(cur, savedSalt, savedHash, savedIter)
            }

            if (!ok) {
                _uiState.update { it.copy(pinChange = it.pinChange.copy(error = "PIN เดิมไม่ถูกต้อง")) }
                return@launch
            }

            val hashed = PinSecurity.hashPin(nw)
            prefs.edit()
                .putString(KEY_PIN_SALT, hashed.saltB64)
                .putString(KEY_PIN_HASH, hashed.hashB64)
                .putInt(KEY_PIN_ITER, hashed.iterations)
                .apply()

            _uiState.update {
                it.copy(pinChange = it.pinChange.copy(success = "เปลี่ยน PIN สำเร็จ", error = null, currentPin = "", newPin = "", confirmPin = ""))
            }
        }
    }

    fun deleteLogoFromServerAndLocal() {
        _uiState.update { it.copy(logoImagePath = null, brandingNonce = System.currentTimeMillis(), message = "ลบโลโก้ในเครื่องแล้ว") }
    }

    fun deleteBackgroundFromServerAndLocal() {
        _uiState.update { it.copy(backgroundImagePath = null, brandingNonce = System.currentTimeMillis(), message = "ลบพื้นหลังในเครื่องแล้ว") }
    }

    // ---------------- Sync ----------------

    fun syncNow() {
        val baseUrl = _uiState.value.serverBaseUrl

        viewModelScope.launch(Dispatchers.IO) {
            _uiState.update { it.copy(isBusy = true, message = null) }

            try {
                val meta = ApiClient.getMeta(baseUrl)
                _uiState.update { it.copy(dbCount = meta.bookCount, lastUpdated = meta.lastUpdated) }

                refreshBranding(baseUrl)

                runCatching {
                    val dtos = ApiClient.fetchAllBooks(baseUrl)
                    val books = dtos.map { dto ->
                        BookEntity(
                            regNo = dto.regNo,
                            title = dto.title,
                            category = dto.category,
                            publisher = dto.publisher,
                            shelf = dto.shelf,
                            coverPath = null
                        )
                    }
                    allBooks = books
                    buildCategoriesAndFilter()
                    _uiState.update { it.copy(lastImportMessage = "Sync จาก Server สำเร็จ (${books.size} รายการ)") }
                }.onFailure { e ->
                    _uiState.update { it.copy(lastImportMessage = "Sync หนังสือล้มเหลว: ${e.message}") }
                }
            } catch (e: Exception) {
                _uiState.update { it.copy(message = "เชื่อมต่อ Server ไม่ได้: ${e.message}") }
            } finally {
                _uiState.update { it.copy(isBusy = false) }
            }
        }
    }

    // 🔥 เปลี่ยนจากโหลดไฟล์ลงเครื่อง เป็นส่ง URL ให้หน้าจอโหลดเองแทน
    private suspend fun refreshBranding(baseUrl: String) {
        try {
            val meta = ApiClient.getBrandingMeta(baseUrl)

            _uiState.update {
                it.copy(
                    // ส่ง URL ตรงไปเลย ให้ Coil จัดการโหลดมาโชว์หน้าจอ
                    logoImagePath = if (meta.hasLogo) "$baseUrl/api/branding/logo" else null,
                    backgroundImagePath = if (meta.hasBackground) "$baseUrl/api/branding/background" else null,
                    brandingNonce = System.currentTimeMillis() // ยิงเวลาใหม่เพื่อให้หน้าจอรู้ว่าต้องเปลี่ยนรูป
                )
            }
        } catch (e: Exception) {
            _uiState.update { it.copy(message = "โหลด Branding ไม่สำเร็จ: ${e.message}") }
        }
    }

    private fun buildCategoriesAndFilter() {
        val cats = allBooks.mapNotNull { it.category?.trim() }
            .filter { it.isNotBlank() }
            .distinct()
            .sorted()

        _uiState.update { it.copy(availableCategories = listOf("ทั้งหมด") + cats) }
        applyFilter()
    }

    private fun applyFilter() {
        val s = _uiState.value
        val q = s.query.trim()
        val cat = s.category.trim()

        val filtered = allBooks.filter { b ->
            val okCat = (cat == "ทั้งหมด") || ((b.category ?: "").trim() == cat)
            if (q.isBlank()) {
                okCat
            } else {
                val hay = listOf(b.regNo, b.title ?: "", b.category ?: "", b.publisher ?: "", b.shelf ?: "").joinToString(" ").lowercase()
                okCat && hay.contains(q.lowercase())
            }
        }
        _uiState.update { it.copy(filteredBooks = filtered) }
    }

    private fun refreshPinState() {
        val now = System.currentTimeMillis() / 1000L
        val until = prefs.getLong(KEY_PIN_LOCK_UNTIL, 0L)
        val fail = prefs.getInt(KEY_PIN_FAIL_COUNT, 0).coerceIn(0, MAX_ATTEMPTS)
        val locked = until > now
        val remainSec = if (locked) (until - now).toInt() else 0
        val left = (MAX_ATTEMPTS - fail).coerceAtLeast(0)

        _uiState.update { it.copy(pinVerify = it.pinVerify.copy(isLocked = locked, lockRemainingSeconds = remainSec, remainingAttempts = left)) }

        if (!locked && fail >= MAX_ATTEMPTS) {
            resetPinLock()
            _uiState.update { it.copy(pinVerify = it.pinVerify.copy(remainingAttempts = MAX_ATTEMPTS)) }
        }
    }

    private fun resetPinLock() {
        prefs.edit().putInt(KEY_PIN_FAIL_COUNT, 0).putLong(KEY_PIN_LOCK_UNTIL, 0L).apply()
    }

    companion object {
        private const val DEFAULT_URL = "http://192.168.1.105:5269"
        private const val KEY_SERVER_URL = "server_url"
        private const val KEY_DISPLAY_NAME = "display_name"
        private const val KEY_PIN_SALT = "pin_salt_b64"
        private const val KEY_PIN_HASH = "pin_hash_b64"
        private const val KEY_PIN_ITER = "pin_iter"
        private const val KEY_PIN_FAIL_COUNT = "pin_fail_count"
        private const val KEY_PIN_LOCK_UNTIL = "pin_lock_until_epoch_sec"
        private const val MAX_ATTEMPTS = 5
        private const val LOCK_SECONDS = 60L
    }
}