"""Makes the Anki test files in tests/Fixtures, using Anki's own official Python library.

Nothing here touches your real Anki: it builds a brand-new, throw-away collection in a temporary
folder, fills it with a few veterinary cards (every card kind, formatting, a list, an image, tags,
a leech, a suspended card, and some reviews with FSRS on), then exports it with Anki's own code in
both the older and the newest file formats.

    python -m venv venv
    venv\\Scripts\\python -m pip install anki
    venv\\Scripts\\python -I tools\\make-anki-fixtures.py tests\\Fixtures
"""

import os
import sys
import tempfile

from anki.collection import Collection, DeckIdLimit, ExportAnkiPackageOptions
from anki.scheduler.v3 import CardAnswer

# A tiny 2x2 PNG picture, so the files stay small.
PNG = bytes.fromhex(
    "89504e470d0a1a0a0000000d49484452000000020000000208020000"
    "00fdd49a730000001649444154789c63f8cfc0f01f0c18fe33300c0c"
    "0c00002f0a05fb8ad26ba50000000049454e44ae426082"
)


def add(col, model_name, deck_id, fields, tags=()):
    note = col.new_note(col.models.by_name(model_name))
    for name, value in fields.items():
        note[name] = value
    note.tags = list(tags)
    col.add_note(note, deck_id)
    return note


def answer(col, deck_id, rating, times=1):
    """Studies the next card of a deck, like pressing a button in Anki."""
    col.decks.select(deck_id)
    for _ in range(times):
        queued = col.sched.get_queued_cards(fetch_limit=1)
        if not queued.cards:
            return
        qc = queued.cards[0]
        card = col.get_card(qc.card.id)
        card.start_timer()   # Anki's review screen does this when it shows a card
        col.sched.answer_card(col.sched.build_answer(card=card, states=qc.states, rating=rating))


def main(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    work = tempfile.mkdtemp(prefix="rev-anki-")
    col = Collection(os.path.join(work, "collection.anki2"))
    col.set_config("fsrs", True)

    pharm = col.decks.id("Vet::Pharmacology")
    para = col.decks.id("Vet::Parasitology")
    image = col.media.write_data("heart.png", PNG)

    # Pharmacology: basic cards with formatting, a list, an image; one reversed note; one type-in.
    add(col, "Basic", pharm, {"Front": "Class of <b>meloxicam</b>?", "Back": "NSAID (<i>COX-2 preferential</i>)"},
        ["pharm", "paper_1"])
    add(col, "Basic", pharm, {"Front": "Signs of NSAID toxicity?",
                              "Back": "<ul><li>vomiting</li><li>GI ulcers</li><li><div>kidney injury</div></li></ul>"},
        ["pharm"])
    add(col, "Basic", pharm, {"Front": "What does this show?", "Back": f'<img src="{image}"><br>A heart &amp; lungs'})
    add(col, "Basic (and reversed card)", pharm, {"Front": "Atipamezole", "Back": "Reverses medetomidine"}, ["pharm"])
    add(col, "Basic (type in the answer)", pharm, {"Front": "Antidote for paracetamol in cats?", "Back": "N-acetylcysteine"})
    leech = add(col, "Basic", pharm, {"Front": "Why avoid ivermectin in Collies?", "Back": "MDR1 mutation"}, ["leech", "pharm"])

    # Parasitology: a cloze note with two cloze numbers, and a basic card to suspend.
    add(col, "Cloze", para, {"Text": "{{c1::Fasciola hepatica}} lives in the {{c2::liver::organ}}",
                             "Back Extra": "Intermediate host: mud snail"}, ["parasites"])
    suspended = add(col, "Basic", para, {"Front": "Vector of Babesia canis?", "Back": "Dermacentor ticks"})
    col.sched.suspend_cards([c.id for c in suspended.cards()])

    # Study a little: two pharmacology cards graduate ("Easy"), one goes into learning ("Good").
    answer(col, pharm, CardAnswer.EASY, times=2)
    answer(col, pharm, CardAnswer.GOOD, times=1)
    answer(col, para, CardAnswer.EASY, times=1)

    vet = col.decks.id("Vet")
    for legacy in (False, True):
        name = "legacy" if legacy else "new"
        col.export_anki_package(
            out_path=os.path.join(out_dir, f"vet-{name}.apkg"),
            options=ExportAnkiPackageOptions(with_scheduling=True, with_deck_configs=False, with_media=True, legacy=legacy),
            limit=DeckIdLimit(vet))
        col.export_collection_package(os.path.join(out_dir, f"vet-{name}.colpkg"), include_media=True, legacy=legacy)
        # Exporting a collection package closes the collection, so open it again.
        col = Collection(os.path.join(work, "collection.anki2"))

    print("leech note guid:", leech.guid)
    col.close()
    for name in sorted(os.listdir(out_dir)):
        print(name, os.path.getsize(os.path.join(out_dir, name)), "bytes")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "tests/Fixtures")
