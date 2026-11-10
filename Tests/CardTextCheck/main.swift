// Checks how the card editor takes pictures out of a card's text and puts them back.
// The iOS build compiles this with Rev/Utilities/CardText.swift and runs it.

func split(_ text: String) -> String { CardText.separatingImages(text).text }
func names(_ text: String) -> [String] { CardText.separatingImages(text).images.map(\.name) }
func editText(of text: String, to new: String) -> String {
    CardText.joiningImages(CardText.separatingImages(text).images, text: new)
}

// The editor's text has no markers, and the pictures are found in order.
precondition(split("[img:a.jpg]") == "")
precondition(names("[img:a.jpg]") == ["a.jpg"])
precondition(split("before\n[img:a.jpg]\nafter\n  [img:b.png]  ") == "before\nafter")
precondition(names("before\n[img:a.jpg]\nafter\n  [img:b.png]  ") == ["a.jpg", "b.png"])

// Saving without changing the text gives back exactly the same card.
for text in ["", "plain", "a\n\nb", "[img:a.jpg]", "[img:a.jpg]\nb", "a\n[img:b.jpg]", "a\n[img:b.jpg]\n\nc",
             "[img:a.jpg]\n[img:b.jpg]\nc", "  [img:a.jpg]  \nb", "*a* [img:not-alone.jpg]", "[img:a.jpg]\n\n"] {
    precondition(editText(of: text, to: split(text)) == text, "changed: \(text)")
}

// Typing keeps pictures where they were, and whatever the box shows stays what it shows.
precondition(editText(of: "a\n[img:b.jpg]\nc", to: "aa\ncc") == "aa\n[img:b.jpg]\ncc")
precondition(editText(of: "[img:a.jpg]", to: "new") == "[img:a.jpg]\nnew")
for new in ["", "x", "x\n", "\n", "x\n\ny\n"] {
    for text in ["[img:a.jpg]", "a\n[img:b.jpg]", "[img:a.jpg]\nb\n[img:c.jpg]"] {
        precondition(split(editText(of: text, to: new)) == new, "box changed: \(text) / \(new)")
    }
}

// Deleting the lines a picture was next to never loses the picture, or swaps pictures around.
precondition(editText(of: "a\nb\nc\n[img:x.jpg]\nd\n[img:y.jpg]", to: "a") == "a\n[img:x.jpg]\n[img:y.jpg]")
precondition(editText(of: "a\n[img:x.jpg]\n[img:y.jpg]", to: "") == "[img:x.jpg]\n[img:y.jpg]")

// Bold or italic over several lines (as Anki writes it) is closed and reopened on each line.
let (b, B, i, I) = (CardText.boldOn, CardText.boldOff, CardText.italicOn, CardText.italicOff)
precondition(CardText.applyingFormatMarkers("\(b)\nDefined as:\n• Analgesia\n• Muscle relaxation\n\n\(B)")
             == "\n**Defined as:**\n• **Analgesia**\n• **Muscle relaxation**\n\n")
precondition(CardText.applyingFormatMarkers("\(i)one\ntwo\(I) three") == "*one*\n*two* three")
precondition(CardText.applyingFormatMarkers("5 * 3\nnext") == "5 * 3\nnext")   // a real asterisk is left alone

print("Card text checks passed")
