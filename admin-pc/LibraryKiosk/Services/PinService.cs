using System;
using System.Security.Cryptography;
using System.Text;
using LibraryKiosk.Models;

namespace LibraryKiosk.Services;

/// <summary>Outcome of a PIN verification attempt.</summary>
public enum PinResultKind { Success, Wrong, Locked }

/// <summary>Result of <see cref="PinService.Verify"/>.</summary>
public readonly struct PinVerifyResult
{
    public PinResultKind Kind { get; }
    /// <summary>Attempts left before lockout (only meaningful for <see cref="PinResultKind.Wrong"/>).</summary>
    public int AttemptsRemaining { get; }
    /// <summary>Seconds remaining on the lock (only meaningful for <see cref="PinResultKind.Locked"/>).</summary>
    public int LockSeconds { get; }

    private PinVerifyResult(PinResultKind kind, int attempts, int lockSeconds)
    {
        Kind = kind;
        AttemptsRemaining = attempts;
        LockSeconds = lockSeconds;
    }

    public static PinVerifyResult Success() => new(PinResultKind.Success, 0, 0);
    public static PinVerifyResult Wrong(int attemptsRemaining) => new(PinResultKind.Wrong, attemptsRemaining, 0);
    public static PinVerifyResult Locked(int lockSeconds) => new(PinResultKind.Locked, 0, lockSeconds);
}

/// <summary>
/// Admin PIN gate (port spec 7). PBKDF2-HMAC-SHA256, 120 000 iterations, 256-bit
/// derived key, 16-byte salt; hash/salt stored Base64 in kiosk-settings.json. When
/// no PIN has ever been set the gate compares against the default "1234".
///
/// Lockout: 5 consecutive wrong attempts → locked 60 s. <c>failCount</c> and
/// <c>lockUntil</c> (Unix epoch seconds) are persisted, so closing and reopening the
/// app cannot reset the lock. When the lock expires the counter resets.
///
/// All comparisons are constant-time (<see cref="CryptographicOperations.FixedTimeEquals"/>)
/// to avoid leaking the PIN through timing.
/// </summary>
public sealed class PinService
{
    public const int Iterations = 120_000;
    private const int SaltSize = 16;   // bytes
    private const int KeySize = 32;    // bytes = 256-bit
    public const int MaxFails = 5;
    public const int LockSeconds = 60;
    public const int MinPinLength = 4;
    public const int MaxPinLength = 8;
    private const string DefaultPin = "1234";

    private readonly SettingsService _settings;

    public PinService(SettingsService settings) => _settings = settings;

    private static long NowEpoch() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>True only the very first time (no custom PIN yet → default "1234").</summary>
    public bool IsDefaultPin
    {
        get
        {
            var s = _settings.Load();
            return string.IsNullOrEmpty(s.PinHash) || string.IsNullOrEmpty(s.PinSalt);
        }
    }

    /// <summary>
    /// Returns true (with seconds remaining) if entry is currently locked. Side effect:
    /// an expired lock is cleared and the fail counter reset.
    /// </summary>
    public bool TryGetLockRemaining(out int secondsRemaining)
    {
        var s = _settings.Load();
        secondsRemaining = 0;
        if (s.LockUntil <= 0) return false;

        var now = NowEpoch();
        if (now >= s.LockUntil)
        {
            s.LockUntil = 0;
            s.FailCount = 0;
            _settings.Save(s);
            return false;
        }

        secondsRemaining = (int)(s.LockUntil - now);
        return true;
    }

    /// <summary>Verify a PIN, applying and persisting the lockout policy.</summary>
    public PinVerifyResult Verify(string pin)
    {
        var s = _settings.Load();
        var now = NowEpoch();

        // Still locked?
        if (s.LockUntil > 0)
        {
            if (now < s.LockUntil)
                return PinVerifyResult.Locked((int)(s.LockUntil - now));
            // Lock expired → reset before evaluating this attempt.
            s.LockUntil = 0;
            s.FailCount = 0;
        }

        if (CheckHash(pin ?? "", s))
        {
            s.FailCount = 0;
            s.LockUntil = 0;
            _settings.Save(s);
            return PinVerifyResult.Success();
        }

        s.FailCount++;
        if (s.FailCount >= MaxFails)
        {
            s.LockUntil = now + LockSeconds;
            _settings.Save(s);
            return PinVerifyResult.Locked(LockSeconds);
        }

        _settings.Save(s);
        return PinVerifyResult.Wrong(MaxFails - s.FailCount);
    }

    /// <summary>Set a new PIN (resets the lockout). Caller validates length/confirmation.</summary>
    public void SetPin(string pin)
    {
        var s = _settings.Load();
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        s.PinSalt = Convert.ToBase64String(salt);
        s.PinHash = Convert.ToBase64String(hash);
        s.PinIterations = Iterations;
        s.FailCount = 0;
        s.LockUntil = 0;
        _settings.Save(s);
    }

    /// <summary>
    /// Reset the PIN to the factory default ("1234") — clears the stored hash/salt and any
    /// lockout (FailCount/LockUntil). Triggered by an admin "reset PIN" broadcast when the
    /// on-site operator forgot the PIN. Touches ONLY the PIN-related fields; every other
    /// setting (resolution, branding-hide, system name, kiosk id, background opacity, …) is
    /// loaded and saved back untouched.
    /// </summary>
    public void ResetToDefault()
    {
        var s = _settings.Load();
        s.PinHash = null;
        s.PinSalt = null;
        s.FailCount = 0;
        s.LockUntil = 0;
        _settings.Save(s);
    }

    public static bool IsValidNewPin(string? pin)
        => pin != null && pin.Length >= MinPinLength && pin.Length <= MaxPinLength
           && pin.Length == CountDigits(pin);

    private static int CountDigits(string s)
    {
        var n = 0;
        foreach (var c in s) if (c >= '0' && c <= '9') n++;
        return n;
    }

    private static bool CheckHash(string pin, KioskSettings s)
    {
        // No custom PIN yet → compare against the default.
        if (string.IsNullOrEmpty(s.PinHash) || string.IsNullOrEmpty(s.PinSalt))
            return FixedTimeEqualsString(pin, DefaultPin);

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(s.PinSalt);
            expected = Convert.FromBase64String(s.PinHash);
        }
        catch
        {
            // Corrupt stored hash → fall back to the default so the kiosk stays openable.
            return FixedTimeEqualsString(pin, DefaultPin);
        }

        var iters = s.PinIterations > 0 ? s.PinIterations : Iterations;
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin), salt, iters, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool FixedTimeEqualsString(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
