# Builds the SideStore source file (source.json) that SideStore reads to show,
# install and update Rev. Values come in from the workflow as $variables.
# The newest version goes first; up to 9 earlier versions (with release notes) are kept as history.
{
  name: "Rev",
  identifier: "com.personal.rev.source",
  subtitle: "Flashcards made for vet school",
  description: "Rev is a calm, thumb-friendly spaced-repetition flashcard app with FSRS scheduling, Anki import and export, cloze cards, tags and images.",
  iconURL: $icon,
  website: $website,
  tintColor: "#E96F8A",
  apps: [
    {
      name: "Rev",
      bundleIdentifier: "com.personal.rev",
      developerName: $owner,
      subtitle: "Spaced-repetition flashcards",
      localizedDescription: "Rev helps you remember what you study.\n\n• Smart scheduling with FSRS (or classic SM-2)\n• Basic, reversed, cloze and type-the-answer cards\n• Tags, filters and a full card browser\n• Import and export Anki decks, with images\n• Stats, streaks and JSON backups",
      iconURL: $icon,
      tintColor: "#E96F8A",
      category: "education",
      version: $version,
      versionDate: $date,
      versionDescription: $notes,
      downloadURL: $url,
      size: $size,
      versions: (
        [
          {
            version: $version,
            buildVersion: $build,
            date: $date,
            localizedDescription: $notes,
            downloadURL: $url,
            size: $size,
            minOSVersion: "17.0"
          }
        ]
        + (($previous[0].apps[0].versions // [])
           | map(select(.version != $version and .localizedDescription != null))
           | .[0:9])
      )
    }
  ],
  news: []
}
