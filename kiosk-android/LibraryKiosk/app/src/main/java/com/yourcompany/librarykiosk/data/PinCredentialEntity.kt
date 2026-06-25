package com.yourcompany.librarykiosk.data

import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "pin_credentials")
data class PinCredentialEntity(
    @PrimaryKey val id: Int = 1,
    val saltBase64: String,
    val hashBase64: String,
    val iterations: Int,
    val failedAttempts: Int,
    val lockedUntilEpochSec: Long
)