#!/usr/bin/env python3
"""Voices for AsciiHero's lines, made with Voicebox (https://github.com/jamiepine/voicebox, MIT).

Voicebox runs on your computer (it needs its TTS models, a few GB, and a GPU or patience); this script asks its local
REST API for every line of the game and puts the results where the game looks for them:

    Unity/Assets/Resources/Voice/<key>.ogg      key = first 12 hex of md5("<speaker>|<text>")  (Game.VoiceKey)

What it voices: every "Speaker: line" of Unity/Assets/Resources/Story/dialogues.txt and Yorick's own remarks
(the Y_* arrays of Unity/Assets/Scripts/GameStory.cs). A line whose file is already there is skipped (delete it to
remake it). Lines of "Рассказчик" are voiced too.

1. Start Voicebox, make a voice profile for each speaker (clone from a sample or pick a preset voice).
2. Edit tools/voices.json: speaker -> profile name (or id). Speakers missing from it use "default".
3. python3 tools/voicebox_voices.py [--api http://127.0.0.1:17493] [--lang ru]
   (needs ffmpeg on PATH to turn the wav into ogg)
"""
import argparse, hashlib, json, os, re, subprocess, sys, time, urllib.request, urllib.error

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIALOGUES = os.path.join(ROOT, "Unity/Assets/Resources/Story/dialogues.txt")
STORY_CS = os.path.join(ROOT, "Unity/Assets/Scripts/GameStory.cs")
OUT = os.path.join(ROOT, "Unity/Assets/Resources/Voice")

def key(who, text):
    return hashlib.md5((who + "|" + text).encode("utf-8")).hexdigest()[:12]

def lines():
    out = []
    for raw in open(DIALOGUES, encoding="utf-8"):
        l = raw.strip()
        if not l or l.startswith("#") or l.startswith("===") or l.startswith("@") or l.startswith("?"): continue
        if l.startswith("["): l = l[l.index("]") + 1:].strip()
        if l.startswith("@") or l.startswith("?"): continue
        c = l.find(":")
        if 0 < c < 40: out.append((l[:c].strip(), l[c + 1:].strip()))
    src = open(STORY_CS, encoding="utf-8").read()
    for arr in re.findall(r"static readonly string\[\] Y_[A-Z0-9]+ = \{(.*?)\};", src, re.S):
        for t in re.findall(r'"((?:[^"\\]|\\.)*)"', arr): out.append(("Череп", t.replace('\\"', '"')))
    seen, res = set(), []
    for w, t in out:
        if (w, t) not in seen: seen.add((w, t)); res.append((w, t))
    return res

def call(api, path, body=None):
    req = urllib.request.Request(api + path, data=None if body is None else json.dumps(body).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=600) as r: return r.status, r.read()

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--api", default="http://127.0.0.1:17493"); ap.add_argument("--lang", default="ru")
    ap.add_argument("--voices", default=os.path.join(ROOT, "tools/voices.json")); a = ap.parse_args()
    voices = json.load(open(a.voices, encoding="utf-8")) if os.path.exists(a.voices) else {}
    _, pj = call(a.api, "/profiles"); profiles = json.loads(pj)
    if isinstance(profiles, dict): profiles = profiles.get("profiles", profiles.get("items", []))
    def pid(name):
        for p in profiles:
            if str(p.get("id")) == name or str(p.get("name", "")).lower() == name.lower(): return p.get("id")
        sys.exit("Нет профиля голоса «%s» в Voicebox (есть: %s)" % (name, ", ".join(str(p.get("name")) for p in profiles)))
    os.makedirs(OUT, exist_ok=True); todo = lines(); print(len(todo), "реплик")
    for i, (who, text) in enumerate(todo):
        k = key(who, text); ogg = os.path.join(OUT, k + ".ogg")
        if os.path.exists(ogg): continue
        prof = voices.get(who, voices.get("default"))
        if not prof: print("  нет голоса для", who, "- пропуск"); continue
        _, gj = call(a.api, "/generate", {"text": text, "profile_id": pid(prof), "language": a.lang})
        gid = json.loads(gj).get("id")
        wav = None
        for _ in range(600):           # wait for the queue (up to 10 min a line)
            try:
                st, data = call(a.api, "/audio/%s" % gid)
                if st == 200 and len(data) > 1000: wav = data; break
            except urllib.error.HTTPError: pass
            time.sleep(1)
        if wav is None: print("  не дождался:", who, text[:40]); continue
        tmp = ogg[:-4] + ".wav"; open(tmp, "wb").write(wav)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", tmp, "-ac", "1", "-ar", "24000", "-c:a", "libvorbis", "-q:a", "4", ogg], check=True)
        os.remove(tmp); print("%3d/%d %s  %s: %s" % (i + 1, len(todo), k, who, text[:50]))

if __name__ == "__main__": main()
