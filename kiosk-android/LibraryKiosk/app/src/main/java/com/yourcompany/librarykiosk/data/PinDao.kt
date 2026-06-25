package com.yourcompany.librarykiosk.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface PinDao {
    @Query("SELECT * FROM pin_credentials WHERE id = 1 LIMIT 1")
    fun get(): PinCredentialEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    fun upsert(entity: PinCredentialEntity)
}