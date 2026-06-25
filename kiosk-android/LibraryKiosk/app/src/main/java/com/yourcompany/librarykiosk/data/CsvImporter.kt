package com.yourcompany.librarykiosk.data

import android.content.ContentResolver
import android.net.Uri

object CsvImporter {

    data class Result(
        val books: List<BookEntity>,
        val imported: Int,
        val skipped: Int,
        val reason: String? = null
    )

    fun importFromUri(cr: ContentResolver, uri: Uri): Result {
        val lines = cr.openInputStream(uri)?.bufferedReader()?.use { it.readLines() }
            ?: return Result(emptyList(), 0, 0, "ไม่สามารถอ่านไฟล์ได้")

        if (lines.isEmpty()) return Result(emptyList(), 0, 0, "ไฟล์ว่าง")

        val header = parseCsvLine(lines.first()).map { it.trim().lowercase() }
        val idxReg = header.indexOf("reg_no").takeIf { it >= 0 } ?: header.indexOf("regno")
        val idxTitle = header.indexOf("title")
        val idxCat = header.indexOf("category")
        val idxPub = header.indexOf("publisher")
        val idxShelf = header.indexOf("shelf")

        if (idxReg < 0 || idxTitle < 0 || idxCat < 0 || idxPub < 0 || idxShelf < 0) {
            return Result(emptyList(), 0, 0, "หัวตารางไม่ถูกต้อง (ต้องมี reg_no,title,category,publisher,shelf)")
        }

        val out = ArrayList<BookEntity>()
        var skipped = 0

        for (i in 1 until lines.size) {
            val row = parseCsvLine(lines[i])
            if (row.size <= maxOf(idxReg, idxTitle, idxCat, idxPub, idxShelf)) {
                skipped++; continue
            }
            val reg = row[idxReg].trim()
            val title = row[idxTitle].trim()
            val cat = row[idxCat].trim()
            val pub = row[idxPub].trim()
            val shelf = row[idxShelf].trim()

            if (reg.isBlank() || title.isBlank()) { skipped++; continue }

            out += BookEntity(
                regNo = reg,
                title = title,
                category = if (cat.isBlank()) "-" else cat,
                shelf = if (shelf.isBlank()) "-" else shelf,
                publisher = if (pub.isBlank()) "-" else pub
            )
        }

        return Result(out, imported = out.size, skipped = skipped, reason = null)
    }

    // รองรับ comma + quote แบบง่าย (พอสำหรับ csv ทั่วไป)
    private fun parseCsvLine(line: String): List<String> {
        val out = mutableListOf<String>()
        val sb = StringBuilder()
        var inQuotes = false
        var i = 0
        while (i < line.length) {
            val c = line[i]
            when {
                c == '"' -> {
                    if (inQuotes && i + 1 < line.length && line[i + 1] == '"') {
                        sb.append('"'); i++
                    } else inQuotes = !inQuotes
                }
                c == ',' && !inQuotes -> {
                    out.add(sb.toString())
                    sb.setLength(0)
                }
                else -> sb.append(c)
            }
            i++
        }
        out.add(sb.toString())
        return out
    }
}