// Checks how Rev reads the saved theme.
// The iOS build compiles this with Rev/Theme/AppTheme.swift and runs it.

// Anything but a known theme means Original, so nobody gets stuck with a missing look.
precondition(AppTheme(stored: nil) == .original)
precondition(AppTheme(stored: "signal") == .signal)
precondition(AppTheme(stored: "original") == .original)
precondition(AppTheme(stored: "neon") == .original)
precondition(AppTheme(stored: "") == .original)
