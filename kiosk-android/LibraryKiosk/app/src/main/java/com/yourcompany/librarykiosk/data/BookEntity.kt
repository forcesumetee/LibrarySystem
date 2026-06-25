package com.yourcompany.librarykiosk.data

import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "books")
data class BookEntity(
    @PrimaryKey val regNo: String,
    val title: String? = null,
    val category: String? = null,
    val publisher: String? = null,
    val shelf: String? = null,
    val coverPath: String? = null
)