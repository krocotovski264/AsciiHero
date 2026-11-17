// Sounds: one-shots (steps, swings, hits, drips, thunder) and looping beds (wind, rain, a stream, birds by day,
// crickets by night, the hearth of the tavern). Everything lies in Resources/Sounds as name_N.ogg: the files that
// share a name before the number are one group, and a group plays one of its files at random, its pitch a little
// different each time (so a step never sounds twice the same). Where a sound comes from (a drip in the cave, a splash)
// sets how loud it is and from which side - by the distance from the hero and its side of the camera.
// Sources and licences: Resources/Sounds/CREDITS.txt.
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public class Sfx
    {
        readonly Dictionary<string, List<AudioClip>> groups = new Dictionary<string, List<AudioClip>>();
        readonly AudioSource[] voices = new AudioSource[14]; int voiceIx;
        class Loop { public AudioSource src; public float target, vol; }
        readonly Dictionary<string, Loop> beds = new Dictionary<string, Loop>();
        readonly GameObject root;
        readonly Dictionary<string, int> lastPick = new Dictionary<string, int>();
        public Vector3 ear; public Vector3 earRight = Vector3.right;
        public int Count => groups.Count;

        public Sfx()
        {
            root = new GameObject("Sounds"); Object.DontDestroyOnLoad(root);
            foreach (var c in Resources.LoadAll<AudioClip>("Sounds"))
            {
                string n = c.name; int us = n.LastIndexOf('_');
                string g = us > 0 && int.TryParse(n.Substring(us + 1), out _) ? n.Substring(0, us) : n;
                List<AudioClip> l; if (!groups.TryGetValue(g, out l)) groups[g] = l = new List<AudioClip>();
                l.Add(c);
            }
            for (int i = 0; i < voices.Length; i++) { var s = root.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0; voices[i] = s; }
            Debug.Log("Звуки: " + groups.Count + " групп");
        }
        public bool Has(string g) { return groups.ContainsKey(g); }

        /// one sound of the group; at = where it is (null: at the hero / no place)
        public void Play(string g, float vol = 1, float pitch = 1, float vary = 0.07f, Vector3? at = null, float delay = 0)
        {
            List<AudioClip> l; if (!groups.TryGetValue(g, out l) || l.Count == 0 || vol <= 0.003f) return;
            int last; lastPick.TryGetValue(g, out last);
            int k = Random.Range(0, l.Count); if (l.Count > 1 && k == last) k = (k + 1) % l.Count; lastPick[g] = k;
            float pan = 0;
            if (at.HasValue)
            {
                Vector3 d = at.Value - ear; float dist = d.magnitude;
                vol *= 1f / (1f + 0.18f * dist * dist * 0.25f); if (vol < 0.01f) return;
                pan = Mathf.Clamp(Vector3.Dot(d, earRight) / Mathf.Max(dist, 1f), -1, 1) * 0.7f;
            }
            // a free voice (or the oldest)
            AudioSource s = null;
            for (int i = 0; i < voices.Length; i++) { var v = voices[(voiceIx + i) % voices.Length]; if (!v.isPlaying) { s = v; voiceIx = (voiceIx + i + 1) % voices.Length; break; } }
            if (s == null) { s = voices[voiceIx]; voiceIx = (voiceIx + 1) % voices.Length; }
            s.clip = l[k]; s.volume = Mathf.Clamp01(vol); s.pitch = pitch * (1 + Random.Range(-vary, vary)); s.panStereo = pan;
            if (delay > 0) s.PlayDelayed(delay); else s.Play();
        }

        /// a looping bed: how loud it should be (it eases there)
        public void Bed(string name, float target)
        {
            Loop b;
            if (!beds.TryGetValue(name, out b))
            {
                if (target <= 0.001f) return;
                List<AudioClip> l; if (!groups.TryGetValue(name, out l) || l.Count == 0) return;
                var s = root.AddComponent<AudioSource>(); s.clip = l[0]; s.loop = true; s.playOnAwake = false; s.volume = 0; s.spatialBlend = 0;
                s.time = Random.value * Mathf.Max(0, l[0].length - 1);
                b = new Loop { src = s }; beds[name] = b;
            }
            b.target = Mathf.Clamp01(target);
        }
        public void Update(float dt)
        {
            foreach (var b in beds.Values)
            {
                b.vol += (b.target - b.vol) * (1 - Mathf.Exp(-dt / 1.2f));
                b.src.volume = b.vol;
                if (b.vol > 0.003f) { if (!b.src.isPlaying) b.src.Play(); }
                else if (b.src.isPlaying) b.src.Pause();
                b.target = 0;           // (asked for again each frame by whoever wants it)
            }
        }
        public void Silence() { foreach (var b in beds.Values) { b.target = 0; } }
    }
}
