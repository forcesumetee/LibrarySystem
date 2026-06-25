using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryKiosk.Services;

namespace LibraryKiosk.ViewModels;

/// <summary>
/// A self-contained numeric keypad (digits 0–9, backspace, confirm). It only
/// accumulates the entry and raises <see cref="Submitted"/>; the policy (verify,
/// lockout, change-PIN steps) lives in <see cref="SettingsViewModel"/>. The owner
/// sets <see cref="Title"/>/<see cref="Subtitle"/>, shows errors via
/// <see cref="SetError"/>, and disables input via <see cref="IsInputEnabled"/>.
/// </summary>
public partial class PinPadViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isInputEnabled = true;

    private string _entry = "";
    /// <summary>Raw entered digits (never shown directly — masked as dots in the UI).</summary>
    public string Entry => _entry;

    /// <summary>One ● per entered digit, for the masked display.</summary>
    public string Mask => new('●', _entry.Length);

    /// <summary>Raised when the user confirms an entry of valid length.</summary>
    public event Action<string>? Submitted;

    [RelayCommand]
    private void Press(string? digit)
    {
        if (!IsInputEnabled || string.IsNullOrEmpty(digit)) return;
        if (_entry.Length >= PinService.MaxPinLength) return;
        _entry += digit;
        OnEntryChanged();
        ClearError();
    }

    [RelayCommand]
    private void Backspace()
    {
        if (!IsInputEnabled || _entry.Length == 0) return;
        _entry = _entry[..^1];
        OnEntryChanged();
    }

    [RelayCommand]
    private void Submit()
    {
        if (!IsInputEnabled) return;
        if (_entry.Length < PinService.MinPinLength)
        {
            SetError($"PIN ต้องมีอย่างน้อย {PinService.MinPinLength} หลัก");
            return;
        }
        Submitted?.Invoke(_entry);
    }

    /// <summary>Reset for a new prompt (clears entry + error, re-enables input).</summary>
    public void Reset(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
        _entry = "";
        OnEntryChanged();
        ClearError();
        IsInputEnabled = true;
    }

    /// <summary>Clear just the entered digits (keeps the prompt), e.g. after a wrong PIN.</summary>
    public void ClearEntry()
    {
        _entry = "";
        OnEntryChanged();
    }

    public void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    public void ClearError()
    {
        ErrorMessage = "";
        HasError = false;
    }

    private void OnEntryChanged() => OnPropertyChanged(nameof(Mask));
}
