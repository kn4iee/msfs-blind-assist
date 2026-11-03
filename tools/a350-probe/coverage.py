"""What the A350's displays draw that MSFS Blind Assist never says (plan 24): for each round of a corpus (one folder per round,
written by `A350Probe displays` with A350_DRAW_JSON set and its output saved as read.txt), each display's drawn texts against
the lines its readers said for it. A text is unsaid when one of its words or numbers appears nowhere in those lines (a dot
leader, a glyph or a reordering is no gap).

usage: python coverage.py CORPUS_DIR OUT_TSV
Prints each display's drawn and unsaid counts; writes OUT_TSV: display, text, colour, rounds unsaid, rounds drawn, x, y.
Standard library only; read-only towards the sim."""
import collections
import glob
import json
import os
import re
import sys

# Each capture file and the read.txt sections its readers write (the EFIS gauge draws the PFD and the ND side by side).
SECTIONS = {
    'EFIS_0': ['Captain PFD', 'Captain ND'], 'EFIS_1': ['First Officer PFD', 'First Officer ND'],
    'MFD_0': ['Captain MFD', 'First Officer MFD'], 'CTR_0': ['E/WD'], 'FCU_0': ['FCU display'],
    'RMP_0': ['RMP 1 display'], 'RMP_1': ['RMP 2 display'], 'RMP_2': ['RMP 3 display'],
    'SFD_0': ['Standby flight display'], 'SND_0': ['Standby navigation display'], 'BAT_0': ['BAT display'],
    'HUD_0': ['Captain HUD display'], 'HUD_1': ['First Officer HUD display'],
}

_WORD = re.compile(r'\d+(?:\.\d+)?|[A-Z]{2,}')


def sections(lines):
    """read.txt's lines by the section ('== Captain PFD') they fall under."""
    out, current = collections.defaultdict(list), None
    for line in lines:
        m = re.match(r'^== (.+?)\s*$', line)
        if m:
            current = m.group(1)
        elif current:
            out[current].append(line.strip())
    return out


def words(text):
    """The words and numbers of a text, upper case; the SND's '|' position separators read as spaces."""
    return _WORD.findall(re.sub(r'\s+', ' ', text.replace('|', ' ')).upper())


def unsaid(text, said_words):
    """Whether a drawn text has a word or number no said line holds (a number's leading zeros aside)."""
    toks = words(text)
    return bool(toks) and any(t not in said_words and t.lstrip('0') not in said_words for t in toks)


def measure(rounds):
    """rounds: (read.txt lines, {display: capture dict}) per round. Returns (drawn, missed, first place) by (display, text, colour)."""
    drawn, missed, place = collections.Counter(), collections.Counter(), {}
    for read_lines, captures in rounds:
        sec = sections(read_lines)
        for display, cap in captures.items():
            said = set(w for name in SECTIONS.get(display, []) for line in sec.get(name, []) for w in words(line))
            said |= set(w.lstrip('0') for w in said)
            for t in cap.get('Texts', []):
                text = re.sub(r'\s+', ' ', t.get('Text', '')).strip().upper()
                if not words(text):
                    continue
                key = (display, text, t.get('ColourName', ''))
                drawn[key] += 1
                if unsaid(text, said):
                    missed[key] += 1
                    place.setdefault(key, (round(t.get('X', 0)), round(t.get('Y', 0))))
    return drawn, missed, place


def _rounds(corpus):
    for d in sorted(glob.glob(os.path.join(corpus, '*'))):
        read = os.path.join(d, 'read.txt')
        if not os.path.isfile(read):
            continue
        caps = {}
        for display in SECTIONS:
            p = os.path.join(d, display + '.json')
            if os.path.isfile(p):
                try:
                    with open(p, encoding='utf-8') as f:
                        caps[display] = json.load(f)
                except (OSError, ValueError):
                    continue
        with open(read, encoding='utf-8', errors='replace') as f:
            yield f.read().splitlines(), caps


def main(argv):
    if len(argv) < 3:
        raise SystemExit('usage: python coverage.py CORPUS_DIR OUT_TSV')
    rounds = list(_rounds(argv[1]))
    if not rounds:
        raise SystemExit(f'No rounds in {argv[1]}: each round is a folder with read.txt and the displays\' .json captures.')
    drawn, missed, place = measure(rounds)
    with open(argv[2], 'w', encoding='utf-8') as f:
        f.write('display\ttext\tcolour\tunsaid\tdrawn\tx\ty\n')
        for key, n in sorted(missed.items(), key=lambda kv: (kv[0][0], -kv[1], kv[0][1])):
            x, y = place[key]
            f.write(f'{key[0]}\t{key[1]}\t{key[2]}\t{n}\t{drawn[key]}\t{x}\t{y}\n')
    print(f'{len(rounds)} rounds')
    for display in SECTIONS:
        total = sum(n for k, n in drawn.items() if k[0] == display)
        gap = sum(n for k, n in missed.items() if k[0] == display)
        print(f'{display:7} drawn {total:6}  unsaid {gap:6}  ({100.0 * gap / max(1, total):.0f}%)')


if __name__ == '__main__':
    main(sys.argv)
