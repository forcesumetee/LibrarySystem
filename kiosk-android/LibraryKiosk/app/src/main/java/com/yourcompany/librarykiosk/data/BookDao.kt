package com.yourcompany.librarykiosk.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface BookDao {
    @Query("SELECT * FROM books ORDER BY regNo")
    suspend fun getAll(): List<BookEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(items: List<BookEntity>)

    @Query("DELETE FROM books")
    suspend fun clearAll()

    @Query("SELECT COUNT(*) FROM books")
    suspend fun count(): Int
}