import XCTest

/// Opens Rev with sample cards and saves a picture of each main screen to $SCREENSHOT_DIR.
final class ScreenshotTests: XCTestCase {
    override func setUp() {
        continueAfterFailure = false
    }

    func testCaptureAll() {
        let looks: [(prefix: String, appearance: XCUIDevice.Appearance, theme: String?)] = [
            ("original-light", .light, nil),
            ("original-dark", .dark, nil),
        ]
        for look in looks {
            XCUIDevice.shared.appearance = look.appearance
            capture(look.prefix, theme: look.theme)
        }
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

        _ = launch(data: "nothingDue", theme: theme)
        shoot("\(prefix)-decks-nothing-due")

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
