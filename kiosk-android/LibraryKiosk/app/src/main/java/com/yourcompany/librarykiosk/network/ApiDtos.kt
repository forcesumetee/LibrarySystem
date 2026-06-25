package com.yourcompany.librarykiosk.network

data class KioskMetaDto(
    val bookCount: Int,
    val lastUpdated: String?,
    val appVersion: String?
)

data class AppConfigDto(
    val displayName: String,
    val updatedAt: String?
)

data class BookDto(
    val regNo: String,
    val title: String?,
    val category: String?,
    val publisher: String?,
    val shelf: String?
)

data class BrandingMetaDto(
    val hasLogo: Boolean,
    val hasBackground: Boolean,
    val updatedAt: String?,
    val logoSha256: String?,
    val backgroundSha256: String?
)