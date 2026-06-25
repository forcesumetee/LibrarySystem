package com.yourcompany.librarykiosk.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color   // ✅ สำคัญ: ต้องมี

private val LightColorScheme = lightColorScheme(
    primary = Brand,
    onPrimary = OnBrand,
    primaryContainer = BrandContainer,
    onPrimaryContainer = OnBrandContainer,

    secondary = Color(0xFF3F4451),
    onSecondary = Color(0xFFFFFFFF),
    secondaryContainer = Surface2,
    onSecondaryContainer = Text,

    background = Bg,
    onBackground = Text,

    surface = Surface,
    onSurface = Text,
    surfaceVariant = Surface2,
    onSurfaceVariant = TextMuted,

    outline = Outline,
    error = Error
)

private val DarkColorScheme = darkColorScheme(
    primary = Color(0xFFBFC2FF),
    onPrimary = Color(0xFF1C1F79),
    primaryContainer = Color(0xFF2F33A6),
    onPrimaryContainer = Color(0xFFE1E1FF),

    background = Color(0xFF0F1014),
    onBackground = Color(0xFFE5E6EC),

    surface = Color(0xFF13141A),
    onSurface = Color(0xFFE5E6EC),
    surfaceVariant = Color(0xFF1B1D25),
    onSurfaceVariant = Color(0xFFBFC3D4),

    outline = Color(0xFF3A3D4A),
    error = Color(0xFFFFB4AB)
)

@Composable
fun LibraryKioskTheme(
    darkTheme: Boolean = false,
    dynamicColor: Boolean = false, // kiosk แนะนำปิดไว้
    content: @Composable () -> Unit
) {
    val colorScheme = if (darkTheme) DarkColorScheme else LightColorScheme

    MaterialTheme(
        colorScheme = colorScheme,
        typography = Typography,
        content = content
    )
}