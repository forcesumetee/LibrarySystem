package com.yourcompany.librarykiosk.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface PinCredentialDao {

    @Query("SELECT * FROM pin_credentials WHERE role = :role LIMIT 1")
    suspend fun getByRole(role: String): PinCredentialEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(entity: PinCredentialEntity)

    @Query("UPDATE pin_credentials SET failedAttempts = :failedAttempts, lockedUntilEpochSec = :lockedUntilEpochSec WHERE role = :role")
    suspend fun updateAttempts(role: String, failedAttempts: Int, lockedUntilEpochSec: Long)
}