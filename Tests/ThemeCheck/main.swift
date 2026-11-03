// Checks how Rev reads the saved theme and fills the Review progress bar.
// The iOS build compiles this with Rev/Theme/AppTheme.swift and Rev/Utilities/ReviewProgress.swift and runs it.

// Anything but a known theme means Original, so nobody gets stuck with a missing look.
precondition(AppTheme(stored: nil) == .original)
precondition(AppTheme(stored: "signal") == .signal)
precondition(AppTheme(stored: "original") == .original)
precondition(AppTheme(stored: "neon") == .original)
precondition(AppTheme(stored: "") == .original)

// The Review progress bar: never more than 30 segments, and full only when the queue is empty.
precondition(ReviewProgress.segments(reviewed: 0, left: 10) == (0, 10))
precondition(ReviewProgress.segments(reviewed: 3, left: 7) == (3, 10))
precondition(ReviewProgress.segments(reviewed: 10, left: 0) == (10, 10))
precondition(ReviewProgress.segments(reviewed: 0, left: 0) == (0, 1))
precondition(ReviewProgress.segments(reviewed: 0, left: 1250) == (0, 30))
precondition(ReviewProgress.segments(reviewed: 1249, left: 1) == (29, 30))
precondition(ReviewProgress.segments(reviewed: 1250, left: 0) == (30, 30))
