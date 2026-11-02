"""Checks that Anki accepts a .apkg made by Rev, using Anki's own official Python library.

It imports the file into a brand-new, throw-away collection in a temporary folder (never your real
Anki), then prints what Anki made of it: decks, note types, every card's text, tags, schedule,
suspended state and FSRS memory, and the pictures. It also imports the file a second time, to check
that Anki updates the same notes instead of adding copies.

    venv\\Scripts\\python -I tools\\check-anki-export.py path\\to\\export.apkg
"""

import os
import sys
import tempfile

from anki.collection import Collection, ImportAnkiPackageOptions, ImportAnkiPackageRequest


def import_once(col, path):
    log = col.import_anki_package(ImportAnkiPackageRequest(
        package_path=path,
        options=ImportAnkiPackageOptions(with_scheduling=True, update_notes=1, update_notetypes=1)))
    return log.log


def main(path):
    col = Collection(os.path.join(tempfile.mkdtemp(prefix="rev-anki-check-"), "collection.anki2"))
    col.set_config("fsrs", True)
    first = import_once(col, os.path.abspath(path))
    print(f"first import: {len(first.new)} new notes, {len(first.updated)} updated, {len(first.duplicate)} duplicates")

    today = col.sched.today
    print("decks:", sorted(d.name for d in col.decks.all_names_and_ids() if d.name != "Default"))
    print("note types:", sorted(m["name"] for m in col.models.all()))
    for cid in sorted(col.find_cards("")):
        card = col.get_card(cid)
        note = card.note()
        memory = card.memory_state
        due = f"in {card.due - today} days" if card.type == 2 else f"new #{card.due}"
        print(f"- [{col.decks.name(card.did)}] {note['Front']!r} -> {note['Back']!r}")
        print(f"    tags={note.tags} type={card.type} queue={card.queue} ivl={card.ivl} ease={card.factor} "
              f"reps={card.reps} lapses={card.lapses} due={due}"
              + (f" fsrs=(s={memory.stability:.2f}, d={memory.difficulty:.2f})" if memory else ""))
    print("media folder:", sorted(os.listdir(col.media.dir())))

    second = import_once(col, os.path.abspath(path))
    print(f"second import: {len(second.new)} new notes, {len(second.updated)} updated, "
          f"{len(second.duplicate)} unchanged; notes in total: {col.note_count()}")
    col.close()


if __name__ == "__main__":
    main(sys.argv[1])
