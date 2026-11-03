import XCTest

/// Opens Rev with sample cards and saves a picture of each main screen to $SCREENSHOT_DIR.
final class ScreenshotTests: XCTestCase {
    override func setUp() {
        continueAfterFailure = false
    }

    /// The workflow sets the simulator to light or dark, then runs this once per look.
    private var look: String { ProcessInfo.processInfo.environment["LOOK"] ?? "original-light" }

    func testCaptureAll() {
        capture(look, theme: look == "signal" ? "signal" : nil)
    }

    func testSignalDeckRowsReadOut() throws {
        try XCTSkipUnless(look == "signal")
        let app = launch(data: "standard", theme: "signal")
        XCTAssertTrue(app.buttons["VPA, 12 to review, 40 cards"].waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["Start review"].exists)
    }

    /// The spec's compact Decks screen: no gap over 12 points between its parts.
    func testSignalDecksGapsStayTight() throws {
        try XCTSkipUnless(look == "signal")
        let app = launch(data: "standard", theme: "signal")
        let start = app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Start review'")).firstMatch
        XCTAssertTrue(start.waitForExistence(timeout: 20))
        let gap = start.frame.minY - app.staticTexts["1,263"].frame.maxY
        XCTAssertLessThanOrEqual(gap, 12.5)
    }

    /// With the phone in light mode, Signal and back to Original must land on the light look.
    func testSwitchingThemeSticksAndGoesBack() throws {
        try XCTSkipUnless(look == "original-light", "Needs the phone in light mode")
        var app = launch(data: "standard", theme: nil)
        tapTab("Settings", in: app)
        tap(themePicker(in: app))
        tap(app.buttons["Signal"])
        tapTab("Decks", in: app)
        shoot("switch-signal")

        app.terminate()
        app = launch(data: "standard", theme: nil)
        tapTab("Settings", in: app)
        let picker = themePicker(in: app)
        XCTAssertTrue(picker.waitForExistence(timeout: 20))
        XCTAssertTrue("\(picker.label) \(picker.value ?? "")".contains("Signal"), "The choice wasn't remembered")

        tap(picker)
        tap(app.buttons["Original"])
        tapTab("Decks", in: app)
        shoot("switch-back-original-light")
        app.terminate()
    }

    private func themePicker(in app: XCUIApplication) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Theme'")).firstMatch
    }

    private func capture(_ prefix: String, theme: String?) {
        var app = launch(data: "standard", theme: theme)
        shoot("\(prefix)-decks")

        tapTab("Review", in: app)
        shoot("\(prefix)-review-question")
        tap(app.buttons["Edit card"])
        shoot("\(prefix)-card-editor")

        app = launch(data: "standard", theme: theme)
        tapTab("Review", in: app)
        tap(app.buttons["Show answer"])
        shoot("\(prefix)-review-answer")

        tapTab("Stats", in: app)
        shoot("\(prefix)-stats")

        tapTab("Settings", in: app)
        shoot("\(prefix)-settings")

        app = launch(data: "standard", theme: theme)
        let deck = app.buttons.matching(NSPredicate(format: "label CONTAINS 'Pharmacology'")).firstMatch
        XCTAssertTrue(deck.waitForExistence(timeout: 20))
        if !deck.isHittable { app.swipeUp() }
        tap(deck)
        shoot("\(prefix)-deck-page")

        _ = launch(data: "empty", theme: theme)
        shoot("\(prefix)-decks-empty")

        app = launch(data: "nothingDue", theme: theme)
        shoot("\(prefix)-decks-nothing-due")
        tapTab("Review", in: app)
        shoot("\(prefix)-review-finished")

        app = launch(data: "cloze", theme: theme)
        tapTab("Review", in: app)
        tap(app.buttons["Edit card"])
        let word = app.textFields["Word to hide"]
        tap(word)
        word.typeText("blood")
        shoot("\(prefix)-card-editor-cloze")

        for (data, name) in [("picture", "picture"), ("long", "long"), ("typeIn", "typein")] {
            app = launch(data: data, theme: theme)
            tapTab("Review", in: app)
            tap(app.buttons["Show answer"])
            shoot("\(prefix)-review-\(name)")
        }
    }

    // MARK: Helpers

    private func launch(data: String, theme: String?) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments = ["-screenshotData", data]
        if let theme { app.launchArguments += ["-appTheme", theme] }
        app.launch()
        return app
    }

    private func tapTab(_ name: String, in app: XCUIApplication) {
        tap(app.tabBars.buttons[name])
    }

    private func tap(_ element: XCUIElement) {
        XCTAssertTrue(element.waitForExistence(timeout: 20), "Not found: \(element)")
        element.tap()
    }

    private func shoot(_ name: String) {
        Thread.sleep(forTimeInterval: 1)   // lets animations finish
        guard let folder = ProcessInfo.processInfo.environment["SCREENSHOT_DIR"] else {
            return XCTFail("SCREENSHOT_DIR isn't set")
        }
        let file = URL(fileURLWithPath: folder).appendingPathComponent("\(name).png")
        XCTAssertNoThrow(try XCUIScreen.main.screenshot().pngRepresentation.write(to: file))
    }
}
