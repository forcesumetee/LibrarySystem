package com.yourcompany.librarykiosk.security

import android.util.Base64
import java.security.SecureRandom
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

object PinSecurity {
    data class HashResult(
        val saltB64: String,
        val hashB64: String,
        val iterations: Int
    )

    private const val KEY_LENGTH_BITS = 256
    private const val DEFAULT_ITERATIONS = 120_000

    fun hashPin(pin: String, iterations: Int = DEFAULT_ITERATIONS): HashResult {
        val salt = ByteArray(16)
        SecureRandom().nextBytes(salt)

        val hash = pbkdf2(pin, salt, iterations)
        return HashResult(
            saltB64 = Base64.encodeToString(salt, Base64.NO_WRAP),
            hashB64 = Base64.encodeToString(hash, Base64.NO_WRAP),
            iterations = iterations
        )
    }

    fun verifyPin(pin: String, saltB64: String, hashB64: String, iterations: Int): Boolean {
        val salt = Base64.decode(saltB64, Base64.NO_WRAP)
        val expected = Base64.decode(hashB64, Base64.NO_WRAP)
        val actual = pbkdf2(pin, salt, iterations)
        return constantTimeEquals(expected, actual)
    }

    private fun pbkdf2(pin: String, salt: ByteArray, iterations: Int): ByteArray {
        val spec = PBEKeySpec(pin.toCharArray(), salt, iterations, KEY_LENGTH_BITS)
        val skf = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256")
        return skf.generateSecret(spec).encoded
    }

    private fun constantTimeEquals(a: ByteArray, b: ByteArray): Boolean {
        if (a.size != b.size) return false
        var r = 0
        for (i in a.indices) r = r or (a[i].toInt() xor b[i].toInt())
        return r == 0
    }
}