package com.yourcompany.librarykiosk.ui

import com.yourcompany.librarykiosk.data.BookEntity

data class UiState(
    // Branding
    val logoImagePath: String? = null,
    val backgroundImagePath: String? = null,
    val brandingNonce: Long = 0L,

    // Header / meta
    val displayName: String = "Library Kiosk",
    val lastUpdated: String? = null,
    val lastImportMessage: String? = null,
    val dbCount: Int = 0,

    // Search / list
    val query: String = "",
    val category: String = "ทั้งหมด",
    val availableCategories: List<String> = listOf("ทั้งหมด"),
    val filteredBooks: List<BookEntity> = emptyList(),

    // UI flags
    val isBusy: Boolean = false,
    val message: String? = null,

    // Book detail
    val selectedBook: BookEntity? = null,
    val coverNonce: Long = 0L,

    // Dialog flags
    val showPinDialog: Boolean = false,
    val showSettingsDialog: Boolean = false,
    val showChangePinDialog: Boolean = false,

    // Settings inputs
    val serverBaseUrl: String = "http://192.168.1.105:5269",
    val serverBaseUrlInput: String = "http://192.168.1.105:5269",
    val displayNameInput: String = "Library Kiosk",

    // PIN forms
    val pinVerify: PinVerifyState = PinVerifyState(),
    val pinChange: PinChangeState = PinChangeState()
)

data class PinVerifyState(
    val input: String = "",
    val error: String? = null,
    val maxAttempts: Int = 5,
    val remainingAttempts: Int = 5,
    val isLocked: Boolean = false,
    val lockRemainingSeconds: Int = 0
)

data class PinChangeState(
    val currentPin: String = "",
    val newPin: String = "",
    val confirmPin: String = "",
    val error: String? = null,
    val success: String? = null
)