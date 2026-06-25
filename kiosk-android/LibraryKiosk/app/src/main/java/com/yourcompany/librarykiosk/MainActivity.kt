package com.yourcompany.librarykiosk

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.lifecycle.viewmodel.compose.viewModel
import coil.compose.AsyncImage
import coil.request.ImageRequest
import coil.request.CachePolicy // 👈 ✅ เริ่มใหม่: นำเข้า CachePolicy เพื่อใช้ปิดแคช
import com.yourcompany.librarykiosk.data.BookEntity
import com.yourcompany.librarykiosk.ui.MainViewModel
import com.yourcompany.librarykiosk.ui.UiState
import com.yourcompany.librarykiosk.ui.theme.LibraryKioskTheme
import java.io.File

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            LibraryKioskTheme(darkTheme = false, dynamicColor = false) {
                Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    LibraryKioskApp()
                }
            }
        }
    }
}

@Composable
fun LibraryKioskApp(vm: MainViewModel = viewModel()) {
    val state by vm.uiState.collectAsState()

    // สั่งให้แอปโหลดข้อมูลและต่อ WebSockets ตั้งแต่เริ่มเปิดแอป
    LaunchedEffect(Unit) { vm.initLoad() }

    Box(Modifier.fillMaxSize()) {
        // ✅ 🚨 เริ่มใหม่: เรียกใช้ BackgroundImage แบบ URL และใส่ nonce 🚨 ✅
        BackgroundImage(url = state.backgroundImagePath, nonce = state.brandingNonce)

        Column(
            modifier = Modifier
                .fillMaxSize()
                // ✅ 🚨 เริ่มใหม่: ปรับ Alpha ของพื้นหลัง (Surface) ให้จางลงเหลือ 0.44f เพื่อให้เห็นภาพพื้นหลังทะลุมา 🚨 ✅
                .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.44f))
                .padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            HeaderSection(
                state = state,
                onOpenSettings = { vm.openPinDialog() }
            )

            SearchAndFilterSection(
                state = state,
                onQueryChange = vm::setQuery,
                onClearQuery = vm::clearQuery,
                onSelectCategory = vm::setCategory
            )

            if (state.isBusy) {
                Card(Modifier.fillMaxWidth()) {
                    Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                        Spacer(Modifier.width(10.dp))
                        Text("กำลังประมวลผล...")
                    }
                }
            }

            state.message?.let { msg ->
                Card(
                    Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.secondaryContainer)
                ) {
                    Row(Modifier.padding(10.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(msg, Modifier.weight(1f))
                        TextButton(onClick = vm::clearMessage) { Text("ปิด") }
                    }
                }
            }

            BookListSection(
                modifier = Modifier.fillMaxSize(),
                state = state,
                onSelectBook = vm::selectBook
            )
        }
    }

    // Dialog รายละเอียดหนังสือ (ไม่ลดทอน)
    state.selectedBook?.let { book ->
        BookDetailDialog(
            book = book,
            totalCount = state.dbCount,
            serverBaseUrl = state.serverBaseUrl, // ส่ง URL เพื่อไปต่อกับ API ดึงรูปปก
            coverNonce = state.coverNonce,
            onDismiss = vm::clearSelectedBook
        )
    }

    // PIN dialog (Admin only) (ไม่ลดทอน)
    if (state.showPinDialog) {
        AdminPinDialog(
            state = state,
            onInputChange = vm::onPinInputChanged,
            onConfirm = vm::verifyAdminPin,
            onDismiss = vm::closePinDialog
        )
    }

    // Settings dialog (ไม่ลดทอน)
    if (state.showSettingsDialog) {
        SettingsDialog(
            state = state,
            onDismiss = vm::closeSettingsDialog,
            onServerUrlChange = vm::setServerBaseUrlInput,
            onSaveServerUrl = vm::saveServerBaseUrl,
            onDisplayNameChange = vm::setDisplayNameInput,
            onSaveDisplayName = vm::saveDisplayNameToServer,
            onSyncNow = vm::syncNow,
            onDeleteLogo = vm::deleteLogoFromServerAndLocal,
            onDeleteBackground = vm::deleteBackgroundFromServerAndLocal,
            onOpenChangePin = vm::openChangePinDialog
        )
    }

    // Change PIN dialog (ไม่ลดทอน)
    if (state.showChangePinDialog) {
        ChangePinDialog(
            state = state,
            onDismiss = vm::closeChangePinDialog,
            onCurrentPinChange = vm::onChangeCurrentPin,
            onNewPinChange = vm::onChangeNewPin,
            onConfirmPinChange = vm::onChangeConfirmPin,
            onSubmit = vm::submitChangePin
        )
    }
}

@Composable
private fun HeaderSection(
    state: UiState,
    onOpenSettings: () -> Unit
) {
    Card(Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp)) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            // ✅ 🚨 เริ่มใหม่: เรียกใช้ LogoImage แบบ URL และใส่ nonce 🚨 ✅
            LogoImage(url = state.logoImagePath, nonce = state.brandingNonce)
            Spacer(Modifier.width(12.dp))

            Column(Modifier.weight(1f)) {
                Text(
                    state.displayName,
                    style = MaterialTheme.typography.titleLarge,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Text("จำนวนหนังสือทั้งหมด: ${state.dbCount}", style = MaterialTheme.typography.bodyMedium)
                Text("อัปเดตล่าสุด: ${state.lastUpdated ?: "-"}", style = MaterialTheme.typography.bodySmall)
                if (!state.lastImportMessage.isNullOrBlank()) {
                    Text(
                        state.lastImportMessage!!,
                        style = MaterialTheme.typography.bodySmall,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }

            Button(onClick = onOpenSettings) { Text("การตั้งค่า") }
        }
    }
}

@Composable
private fun SearchAndFilterSection(
    state: UiState,
    onQueryChange: (String) -> Unit,
    onClearQuery: () -> Unit,
    onSelectCategory: (String) -> Unit
) {
    Card(Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp)) {
        Column(Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(
                value = state.query,
                onValueChange = onQueryChange,
                modifier = Modifier.fillMaxWidth(),
                label = { Text("ค้นหา (เลขทะเบียน / ชื่อ / หมวด / ชั้น / สำนักพิมพ์)") },
                singleLine = true
            )

            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                CategoryDropdown(
                    categories = state.availableCategories,
                    selectedCategory = state.category,
                    onSelectCategory = onSelectCategory,
                    modifier = Modifier.weight(1f)
                )
                OutlinedButton(onClick = onClearQuery) { Text("ล้างคำค้น") }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CategoryDropdown(
    categories: List<String>,
    selectedCategory: String,
    onSelectCategory: (String) -> Unit,
    modifier: Modifier = Modifier
) {
    var expanded by remember { mutableStateOf(false) }

    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { expanded = !expanded },
        modifier = modifier
    ) {
        OutlinedTextField(
            value = selectedCategory,
            onValueChange = {},
            readOnly = true,
            singleLine = true,
            label = { Text("หมวดหมู่") },
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
            modifier = Modifier.menuAnchor().fillMaxWidth()
        )

        ExposedDropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false }
        ) {
            categories.forEach { cat ->
                DropdownMenuItem(
                    text = { Text(cat) },
                    onClick = {
                        onSelectCategory(cat)
                        expanded = false
                    }
                )
            }
        }
    }
}

@Composable
private fun BookListSection(modifier: Modifier, state: UiState, onSelectBook: (BookEntity) -> Unit) {
    Card(modifier.fillMaxSize(), shape = RoundedCornerShape(16.dp)) {
        Column(Modifier.fillMaxSize()) {
            Text(
                "รายการหนังสือ (${state.filteredBooks.size})",
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier.padding(12.dp)
            )
            HorizontalDivider()

            if (state.filteredBooks.isEmpty()) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { Text("ไม่พบข้อมูล") }
            } else {
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    items(state.filteredBooks, key = { it.regNo }) { book ->
                        BookRowCard(book = book, onClick = { onSelectBook(book) })
                    }
                }
            }
        }
    }
}

@Composable
private fun BookRowCard(book: BookEntity, onClick: () -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth().clickable { onClick() },
        shape = RoundedCornerShape(14.dp)
    ) {
        Column(Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Text(book.title ?: "-", style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text("เลขทะเบียน: ${book.regNo}", style = MaterialTheme.typography.bodySmall)
            Text("หมวด: ${book.category ?: "-"}", style = MaterialTheme.typography.bodySmall)
            Text("ชั้นวาง: ${book.shelf ?: "-"}", style = MaterialTheme.typography.bodySmall)
            Text("สำนักพิมพ์: ${book.publisher ?: "-"}", style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun BookDetailDialog(
    book: BookEntity,
    totalCount: Int,
    serverBaseUrl: String,
    coverNonce: Long,
    onDismiss: () -> Unit
) {
    Dialog(onDismissRequest = onDismiss) {
        Surface(shape = RoundedCornerShape(16.dp), tonalElevation = 2.dp, modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("รายละเอียดหนังสือ", style = MaterialTheme.typography.titleLarge, modifier = Modifier.weight(1f))
                    TextButton(onClick = onDismiss) { Text("ปิด") }
                }

                Card(shape = RoundedCornerShape(12.dp)) {
                    // โหลดหน้าปกจาก Server ตรงๆ และใช้ ?t=... เพื่อหลีกเลี่ยง Cache
                    AsyncImage(
                        model = ImageRequest.Builder(LocalContext.current)
                            .data("$serverBaseUrl/api/books/${book.regNo}/cover?t=$coverNonce")
                            .crossfade(true)
                            .build(),
                        contentDescription = "cover",
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxWidth().height(180.dp)
                    )
                }

                HorizontalDivider()
                DetailRow("เลขทะเบียน", book.regNo)
                DetailRow("ชื่อหนังสือ", book.title ?: "-")
                DetailRow("หมวดหมู่", book.category ?: "-")
                DetailRow("ชั้นวาง", book.shelf ?: "-")
                DetailRow("สำนักพิมพ์", book.publisher ?: "-")
                HorizontalDivider()
                Text("จำนวนข้อมูลในเครื่อง: $totalCount", style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}

@Composable
private fun DetailRow(label: String, value: String) {
    Column(Modifier.padding(vertical = 6.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium)
        Text(value, style = MaterialTheme.typography.bodyLarge)
    }
}

// ------------------------------------------------------
// 🔥 ✅ เริ่มใหม่: โซนจัดการ Logo และ Background แบบห้ามจำแคชเด็ดขาด ✅ 🔥
// ------------------------------------------------------

// 🖼️ ✅ เริ่มใหม่: ดึงโลโก้จาก URL โดยตรง และสั่งปิดแคชแบบถอนรากถอนโคน
@Composable
private fun LogoImage(url: String?, nonce: Long) {
    Card(shape = RoundedCornerShape(12.dp), modifier = Modifier.size(56.dp)) {
        // 🔥 ใช้ key บังคับให้โหลดใหม่ทันทีเมื่อมีการอัปเดต nonce
        key(nonce) {
            if (!url.isNullOrBlank()) {
                AsyncImage(
                    model = ImageRequest.Builder(LocalContext.current)
                        .data("$url?t=$nonce") // ✅ แนบ nonce ไปที่ URL ด้วย
                        // ❌ ✅ เริ่มใหม่: สั่งปิดการจำไฟล์ภาพใน RAM และในเครื่องเด็ดขาด ✅ ❌
                        .memoryCachePolicy(CachePolicy.DISABLED)
                        .diskCachePolicy(CachePolicy.DISABLED)
                        .crossfade(true)
                        .build(),
                    contentDescription = "logo",
                    contentScale = ContentScale.Crop,
                    modifier = Modifier.fillMaxSize()
                )
            } else {
                Box(
                    Modifier.fillMaxSize().background(MaterialTheme.colorScheme.primaryContainer),
                    contentAlignment = Alignment.Center
                ) { Text("LK") }
            }
        }
    }
}

// 🏞️ ✅ เริ่มใหม่: ดึงพื้นหลังจาก URL โดยตรง และสั่งปิดแคชแบบถอนรากถอนโคน
@Composable
private fun BackgroundImage(url: String?, nonce: Long) {
    // 🔥 ใช้ key บังคับให้โหลดใหม่ทันทีเมื่อมีการอัปเดต nonce
    key(nonce) {
        if (!url.isNullOrBlank()) {
            AsyncImage(
                model = ImageRequest.Builder(LocalContext.current)
                    .data("$url?t=$nonce") // ✅ แนบ nonce ไปที่ URL ด้วย
                    // ❌ ✅ เริ่มใหม่: สั่งปิดการจำไฟล์ภาพใน RAM และในเครื่องเด็ดขาด ✅ ❌
                    .memoryCachePolicy(CachePolicy.DISABLED)
                    .diskCachePolicy(CachePolicy.DISABLED)
                    .crossfade(true)
                    .build(),
                contentDescription = "background",
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxSize()
            )
        } else {
            // ถ้าไม่มีรูปพื้นหลัง ให้เทสีดำลงไปก่อนเพื่อให้ UI ไม่ดูโล่งเกินไป
            Box(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background))
        }
    }
}

// 🚨 โซน PIN (ไม่ลดทอน)
@Composable
private fun AdminPinDialog(
    state: UiState,
    onInputChange: (String) -> Unit,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit
) {
    val p = state.pinVerify
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("ยืนยันรหัสก่อนเข้าตั้งค่า") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = p.input,
                    onValueChange = { onInputChange(it.filter(Char::isDigit)) },
                    label = { Text("PIN (ADMIN)") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    enabled = !p.isLocked,
                    modifier = Modifier.fillMaxWidth()
                )

                if (p.isLocked) {
                    Text("ล็อกชั่วคราว กรุณารอ ${p.lockRemainingSeconds} วินาที", color = MaterialTheme.colorScheme.error)
                } else {
                    Text("ใส่ผิดได้อีก ${p.remainingAttempts}/${p.maxAttempts} ครั้ง", style = MaterialTheme.typography.bodySmall)
                }
                p.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = { Button(onClick = onConfirm, enabled = !p.isLocked) { Text("ยืนยัน") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("ยกเลิก") } }
    )
}

// 🚨 โซน Settings (ไม่ลดทอน)
@Composable
private fun SettingsDialog(
    state: UiState,
    onDismiss: () -> Unit,
    onServerUrlChange: (String) -> Unit,
    onSaveServerUrl: () -> Unit,
    onDisplayNameChange: (String) -> Unit,
    onSaveDisplayName: () -> Unit,
    onSyncNow: () -> Unit,
    onDeleteLogo: () -> Unit,
    onDeleteBackground: () -> Unit,
    onOpenChangePin: () -> Unit
) {
    var showAbout by remember { mutableStateOf(false) }

    if (showAbout) {
        AlertDialog(
            onDismissRequest = { showAbout = false },
            title = { Text("เกี่ยวกับ") },
            text = {
                val ctx = LocalContext.current
                val versionText = remember {
                    try {
                        val pInfo = ctx.packageManager.getPackageInfo(ctx.packageName, 0)
                        val name = pInfo.versionName ?: "1.0.0"
                        val code = if (android.os.Build.VERSION.SDK_INT >= 28) pInfo.longVersionCode else pInfo.versionCode.toLong()
                        "$name ($code)"
                    } catch (_: Exception) {
                        "1.0.0"
                    }
                }

                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text("Kiosk library search", style = MaterialTheme.typography.titleMedium)
                    Text("NTY MULTIMEDIA CO.,LTD.", style = MaterialTheme.typography.bodyMedium)
                    Text(
                        "Copyright © 2026 NTY MULTIMEDIA CO.,LTD. All rights reserved.",
                        style = MaterialTheme.typography.bodySmall
                    )
                    Spacer(Modifier.height(8.dp))
                    Text("Version: $versionText", style = MaterialTheme.typography.bodySmall)
                }
            },
            confirmButton = {
                Button(onClick = { showAbout = false }) { Text("ปิด") }
            }
        )
    }

    Dialog(onDismissRequest = onDismiss) {
        Surface(shape = RoundedCornerShape(16.dp), tonalElevation = 2.dp, modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(14.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text("ตั้งค่า Kiosk", style = MaterialTheme.typography.titleLarge)

                Text("Server", style = MaterialTheme.typography.titleMedium)
                OutlinedTextField(
                    value = state.serverBaseUrlInput,
                    onValueChange = onServerUrlChange,
                    label = { Text("Base URL เช่น http://192.168.1.105:5269") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = onSaveServerUrl, modifier = Modifier.weight(1f)) { Text("บันทึก URL") }
                    Button(onClick = onSyncNow, modifier = Modifier.weight(1f)) { Text("Sync Now") }
                }

                HorizontalDivider()

                Text("ชื่อระบบ", style = MaterialTheme.typography.titleMedium)
                OutlinedTextField(
                    value = state.displayNameInput,
                    onValueChange = onDisplayNameChange,
                    label = { Text("ชื่อที่จะแสดงบนหน้าจอ") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                Button(onClick = onSaveDisplayName, modifier = Modifier.fillMaxWidth()) { Text("บันทึกชื่อระบบ (ไปที่ Server)") }

                HorizontalDivider()

                Text("Branding", style = MaterialTheme.typography.titleMedium)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = onDeleteLogo, modifier = Modifier.weight(1f)) { Text("ลบโลโก้") }
                    OutlinedButton(onClick = onDeleteBackground, modifier = Modifier.weight(1f)) { Text("ลบพื้นหลัง") }
                }

                HorizontalDivider()

                Text("ข้อมูล", style = MaterialTheme.typography.titleMedium)
                Text(
                    "การนำเข้าหนังสือจะทำผ่านโปรแกรม Admin-PC เท่านั้น\nKiosk ใช้สำหรับ Sync/ค้นหา/แสดงผล",
                    style = MaterialTheme.typography.bodySmall
                )

                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = onOpenChangePin, modifier = Modifier.weight(1f)) { Text("เปลี่ยน PIN") }
                    OutlinedButton(onClick = { showAbout = true }, modifier = Modifier.weight(1f)) { Text("เกี่ยวกับ") }
                }

                HorizontalDivider()
                Text("สถานะ", style = MaterialTheme.typography.titleMedium)
                Text("จำนวนหนังสือในเครื่อง: ${state.dbCount}")
                Text("อัปเดตล่าสุด: ${state.lastUpdated ?: "-"}")

                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = onDismiss, modifier = Modifier.weight(1f)) { Text("ปิด") }
                }
            }
        }
    }
}

// 🚨 โซน Change PIN (ไม่ลดทอน)
@Composable
private fun ChangePinDialog(
    state: UiState,
    onDismiss: () -> Unit,
    onCurrentPinChange: (String) -> Unit,
    onNewPinChange: (String) -> Unit,
    onConfirmPinChange: (String) -> Unit,
    onSubmit: () -> Unit
) {
    val f = state.pinChange
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("เปลี่ยน PIN (ADMIN)") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = f.currentPin,
                    onValueChange = { onCurrentPinChange(it.filter(Char::isDigit)) },
                    label = { Text("รหัสเดิม") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = f.newPin,
                    onValueChange = { onNewPinChange(it.filter(Char::isDigit)) },
                    label = { Text("รหัสใหม่ (4-8 หลัก)") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = f.confirmPin,
                    onValueChange = { onConfirmPinChange(it.filter(Char::isDigit)) },
                    label = { Text("ยืนยันรหัสใหม่") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    modifier = Modifier.fillMaxWidth()
                )

                f.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                f.success?.let { Text(it, color = MaterialTheme.colorScheme.primary) }
            }
        },
        confirmButton = { Button(onClick = onSubmit) { Text("บันทึก") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("ปิด") } }
    )
}