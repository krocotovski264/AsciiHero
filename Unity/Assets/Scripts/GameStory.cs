// The story: the first location drawn in the map editor (Resources/Maps/start.json), its people and creatures, and
// the scenes played in it (Resources/Story/dialogues.txt).
//   * the dungeon (caves, the meadow round them) is one place; every inn is a place of its own, entered through a hole
//     in the rock (the rats' den) or a door (the «Пьяный дракон»). A place the hero leaves is only switched off: its
//     creatures, its corpses, its open chests are as he left them when he comes back.
//   * scenes come in four tiers (see the design doc «Диалоги в движении»): T0 a line over a head, T1 subtitles while
//     the hero walks on, T2 the same but at a walk, T3 a full scene - black bars, the hero is not his own, it can be skipped.
//   * the rat-men: a man's body with fur, a rat's head (3moji, CC BY-SA 4.0) and a tail; they wander, look about, see
//     only what is in front of them and not through walls, come at the hero they saw, strike, now and then leap back.
//     Their chief is a fifth taller and fights with his head: he reads the hero's guard, parries and answers at once,
//     side-steps a blow, circles, strikes in combinations, and gets reckless when badly hurt.
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public partial class Game
    {
        // ================= the places =================
        bool story; StoryMap smap; StoryLocation sMain; readonly List<StoryLocation> sLocs = new List<StoryLocation>();
        readonly List<StoryPassage> sPass = new List<StoryPassage>();
        readonly HashSet<string> sFlags = new HashSet<string>(), sDone = new HashSet<string>();
        float passCd; string sBiome; bool sHasSkull;
        static readonly Vector3 STORY_AT = new Vector3(0, 0, 4000);

        StoryLocation SLoc(string region) { foreach (var l in sLocs) if (l.region == region) return l; return null; }

        void BuildStory()
        {
            smap = StoryMap.Load("Maps/start");
            // the inns: runs of tavern tiles joined side to side
            var tavern = new Dictionary<long, StoryMap.Tile>(); var other = new HashSet<long>();
            foreach (var t in smap.tiles) if (t.b == "tavern") tavern[StoryLocation.Key(t.i, t.j)] = t; else other.Add(StoryLocation.Key(t.i, t.j));
            var regions = new List<HashSet<long>>(); var seen = new HashSet<long>();
            foreach (var kv in tavern)
            {
                if (seen.Contains(kv.Key)) continue;
                var set = new HashSet<long>(); var q = new Queue<StoryMap.Tile>(); q.Enqueue(kv.Value); seen.Add(kv.Key);
                while (q.Count > 0)
                {
                    var t = q.Dequeue(); set.Add(StoryLocation.Key(t.i, t.j));
                    foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                    {
                        long nk = StoryLocation.Key(t.i + d.x, t.j + d.y); StoryMap.Tile nt;
                        if (!seen.Contains(nk) && tavern.TryGetValue(nk, out nt)) { seen.Add(nk); q.Enqueue(nt); }
                    }
                }
                regions.Add(set);
            }
            // the ways in: where an inn's tile meets a tile of the dungeon - the middle edge of the longest shared run
            sPass.Clear();
            var byBiome = new Dictionary<long, string>(); foreach (var t in smap.tiles) byBiome[StoryLocation.Key(t.i, t.j)] = t.b;
            for (int r = 0; r < regions.Count; r++)
            {
                var edges = new List<KeyValuePair<Vector2, Vector2>>(); var outerB = new List<string>();
                foreach (var key in regions[r])
                {
                    var t = tavern[key];
                    foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                    {
                        long nk = StoryLocation.Key(t.i + d.x, t.j + d.y); string nb;
                        if (!other.Contains(nk) || !byBiome.TryGetValue(nk, out nb)) continue;
                        var c = new Vector2((t.i + 0.5f) * smap.tile, (t.j + 0.5f) * smap.tile) + new Vector2(d.x, d.y) * smap.tile * 0.5f;
                        edges.Add(new KeyValuePair<Vector2, Vector2>(c, new Vector2(-d.x, -d.y))); outerB.Add(nb);      // n: from the dungeon into the inn
                    }
                }
                if (edges.Count == 0) continue;
                // prefer an edge with no wall drawn across it, nearest the middle of them all
                Vector2 mid = Vector2.zero; foreach (var e in edges) mid += e.Key; mid /= edges.Count;
                int best = 0; float bs = 1e9f;
                for (int i = 0; i < edges.Count; i++)
                {
                    float s = (edges[i].Key - mid).magnitude;
                    foreach (var w in smap.walls) if (SegDist(edges[i].Key, w.a, w.b) < 0.8f) s += 50;
                    if (s < bs) { bs = s; best = i; }
                }
                sPass.Add(new StoryPassage { p = edges[best].Key, n = edges[best].Value, from = "main", to = "inn" + r, style = outerB[best] == "cave" ? "hole" : "door" });
            }
            // the places, far from the test world (each one a little apart)
            var mainSet = new HashSet<long>(other);
            sLocs.Clear();
            sMain = StoryLocation.Build(mats, smap, "main", mainSet, true, sPass, STORY_AT, 11);
            sLocs.Add(sMain);
            for (int r = 0; r < regions.Count; r++) sLocs.Add(StoryLocation.Build(mats, smap, "inn" + r, regions[r], false, sPass, STORY_AT + new Vector3(600 + 400 * r, 0, 0), 20 + r));
            foreach (var l in sLocs)
            {
                l.title = l == sMain ? "Подземелье" : "Трактир";
                foreach (var g in l.passages) if (g.to == l.region) l.title = g.style == "hole" ? "Крысиный трактир" : "Трактир «Пьяный дракон»";
                l.ambSky = Mats.Hex(0x40506a) * 0.16f; l.ambEq = Mats.Hex(0x303840) * 0.14f; l.ambGround = Mats.Hex(0x181410) * 0.12f; l.moonK = 0; l.heroLightK = 1.05f; l.exposure = 1.3f;
                GlyphLayer.Build(l.root);
                CollectGlows(l.root);
                foreach (var g in l.passages) if (g.to == l.region) l.spawn = l.ToWorld(g.p + g.n * 1.3f) - l.origin;     // (an inn: by its way in)
                l.SetActive(false);
            }
            LoadScenes();
            SpawnStoryThings();
        }
        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)); return (a + ab * t - p).magnitude;
        }
        /// which place a point of the map belongs to
        StoryLocation SLocAt(Vector2 m)
        {
            foreach (var l in sLocs) if (l != sMain && l.Walk(m)) return l;
            return sMain;
        }

        /// the new game (or the game going on, if one is): the hero wakes in the dungeon
        void StartStory(bool fresh)
        {
            try { if (tavern == null) SetupWorld(); if (smap == null) BuildStory(); }
            catch (System.Exception e) { Debug.LogException(e); ShowToast("Не удалось собрать локацию"); return; }
            if (fresh && sDone.Count > 0) { ResetStory(); }
            bool first = !sDone.Contains("wake");
            Vector3 at = sMain.SpawnW;
            foreach (var e in smap.ents) if (e.type == "hero") { at = sMain.ToWorld(e.p); sMain.spawn = at - sMain.origin; }
            if (!first && sLast != null) { StoryGo(sLast, sLastPos, sLastHeading); return; }
            if (first) { RestoreBody(); if (player != null) player.SetLimbs(limbs); }
            StoryGo(sMain, at, Mathf.PI * 0.5f);
            if (first) { player.PlayCut("wake", true); Play("wake"); }
        }
        StoryLocation sLast; Vector3 sLastPos; float sLastHeading;
        void ResetStory()
        {
            foreach (var l in sLocs) if (l.root != null) Destroy(l.root);
            sLocs.Clear(); smap = null; sMain = null; foes.Clear(); folk.Clear(); chests.Clear(); sFlags.Clear(); sDone.Clear(); sHasSkull = false; skullGo = null;
            inventory.Remove("Череп Ёрика"); sLast = null; sDir = null; queue.Clear(); barks.Clear();
            BuildStory();
        }

        /// put the hero into one of the story's places (the others are switched off, as they are)
        void StoryGo(StoryLocation L, Vector3 at, float heading)
        {
            SaveCfg();
            story = true; world = true;
            if (tavern != null) { tavern.SetActive(false); bartenderChar.go.SetActive(false); }
            foreach (var l in outdoor) l.SetActive(false);
            foreach (var l in sLocs) l.SetActive(l == L);
            loc = L; sBiome = null;
            worldLightsRoot.SetActive(true); editorRoot.SetActive(false);
            pos = at; heroTrail = pos; player.heading = heading; player.ResetSim(); player.noLock = false;
            hero.go.transform.position = pos; hero.go.transform.rotation = Quaternion.Euler(0, heading * Mathf.Rad2Deg, 0);
            camTgt = new Vector3(pos.x, 0.6f, pos.z);
            sceneCam.fieldOfView = 30; ascii.exposure = 1.3f; ascii.actorBoost = 1; Shader.SetGlobalFloat("_AsciiLod", ISO_LOD); Shader.SetGlobalFloat("_GlyphFarRef", GLYPH_FAR);
            SetShadowDistance(26);
            StoryLight(true);
            ShowToast(L.TitleAt(pos));
        }
        /// the light follows the ground under the hero: dark caves (his own glow), the night meadow, the warm inn
        void StoryLight(bool force)
        {
            var L = loc as StoryLocation; if (L == null) return;
            string b = L.BiomeAtW(pos) ?? sBiome ?? "cave";
            if (!force && b == sBiome) return;
            sBiome = b;
            Sky.Neutral();
            if (b == "meadow")
            {
                if (moonLight != null) { moonLight.enabled = true; moonLight.intensity = 0.35f; moonLight.color = Mats.Hex(0x8aa6ff); moonLight.transform.rotation = Quaternion.LookRotation(-new Vector3(-0.3f, 0.75f, -0.5f).normalized); }
                heroLight.intensity = 1.3f; SetAmbient(Mats.Hex(0x50607e) * 0.4f, Mats.Hex(0x343c44) * 0.35f, Mats.Hex(0x141a10) * 0.3f);
            }
            else if (b == "tavern")
            {
                if (moonLight != null) moonLight.enabled = false;
                heroLight.intensity = 1.5f; SetAmbient(Mats.Hex(0x50465a) * 0.45f, Mats.Hex(0x342b32) * 0.45f, Mats.Hex(0x18100a) * 0.45f);
            }
            else
            {
                if (moonLight != null) moonLight.enabled = false;
                heroLight.intensity = 1.6f * L.heroLightK; SetAmbient(L.ambSky, L.ambEq, L.ambGround);
            }
            if (precipGo != null) precipGo.SetActive(false);
        }
        void StoryHide()
        {
            foreach (var l in sLocs) l.SetActive(false);
            if (story && loc is StoryLocation) { sLast = (StoryLocation)loc; sLastPos = pos; sLastHeading = player != null ? player.heading : 0; }
            story = false; letter = 0; sDir = null;
        }

        /// through a hole or a door: the picture is retyped, the other place comes in behind it
        void StoryPassages(float dt)
        {
            passCd -= dt; var L = loc as StoryLocation; if (L == null || passCd > 0 || sDir != null && sDir.tier == 3) return;
            var m = L.ToMap(pos);
            foreach (var g in L.passages)
            {
                if ((m - g.p).magnitude > 1.05f) continue;
                bool fromHere = g.from == L.region; var into = fromHere ? g.n : -g.n;       // the way through, from this side
                var v = player.Velocity; if (new Vector2(-v.x, v.z).sqrMagnitude > 0.01f && Vector2.Dot(new Vector2(-v.x, v.z).normalized, into) < 0.2f) continue;
                var other = SLoc(fromHere ? g.to : g.from); if (other == null) continue;
                passCd = 1.5f;
                var target = other.ToWorld(g.p + into * 1.3f); float hd = Mathf.Atan2(-into.x, into.y);
                Reprint(() => StoryGo(other, target, hd));
                Sound(g.style == "hole" ? "hit_stone" : "door_open", 0.6f, pos);
                return;
            }
        }

        // ================= the people and the creatures =================
        class Foe
        {
            public string id, name, kind; public Character ch; public MotionActor act; public StoryLocation L;
            public Vector3 p, home, goal; public int hp, maxHp; public float scale = 1;
            public int state; public float t, cd, seenT, lostT, dodgeCd, parryCd, leapT, stagger, deadT, barkCd, guardT, strafe = 1, dmgK = 1, think;
            public Vector2 leapV; public bool noticed, hurtBark, swingHit, raged; public int combo; public Vector3 lastSeen;
            public bool Dead => hp <= 0;
            public bool Boss => kind == "boss";
        }
        class Folk { public string id, name, scene; public Character ch; public MotionActor act; public StoryLocation L; public Vector3 p; public float yaw, barkT, faceT; public bool drunk; }
        class Chest { public string id, name, note; public GameObject go, lid; public StoryLocation L; public Vector3 p; public bool open; public float openT; }
        readonly List<Foe> foes = new List<Foe>(); readonly List<Folk> folk = new List<Folk>(); readonly List<Chest> chests = new List<Chest>();
        GameObject skullGo; StoryLocation skullL; float skullYaw, skullYawT;
        static Mesh ratHead;

        void SpawnStoryThings()
        {
            foes.Clear(); folk.Clear(); chests.Clear();
            var R = new System.Random(5);
            foreach (var e in smap.ents)
            {
                var L = SLocAt(e.p); var w = L.ToWorld(e.p);
                try
                {
                    if (e.type == "enemy")
                    {
                        string kind = e.name.Contains("Босс") ? "boss" : e.name.Contains("зомби") ? "zombie" : "rat";
                        foes.Add(MakeFoe(e.id, e.name, kind, L, w, (float)R.NextDouble() * 6.28f));
                    }
                    else if (e.type == "npc" && e.name.Contains("Череп"))
                    {
                        skullL = L; skullGo = MeshGen.Obj(Cave.CaveMesh("skull", Vector3.one * 1.05f), mats["BONE"], L.root.transform, w - L.origin + Vector3.down * 0.015f, Quaternion.identity, true, "Yorick");
                        GlyphLayer.Build(skullGo);
                        // turned away from where the hero wakes
                        Vector3 heroAt = w; foreach (var h in smap.ents) if (h.type == "hero") heroAt = L.ToWorld(h.p);
                        var d = heroAt - w; skullYaw = skullYawT = Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;
                    }
                    else if (e.type == "npc")
                    {
                        bool drunk = e.name.Contains("крысолюд");
                        var f = drunk ? MakeFoe(e.id, e.name, "drunk", L, w, (float)R.NextDouble() * 6.28f) : null;
                        if (drunk) { foes.Add(f); continue; }
                        var cfgN = new CharConfig { sex = e.name.Contains("Хозяйка") ? 'f' : 'm', build = e.name.Contains("Хозяин") ? 3 : e.name.Contains("Пьяница") ? 0 : 1, height = e.name.Contains("Хозяйка") ? 166 : 176, armor = "cloth", weapon = "none" };
                        var ch = Character.Build(cfgN, actorMats, e.name); ch.go.transform.SetParent(L.root.transform, true); ch.go.transform.position = w;
                        GlyphLayer.Build(ch.go);
                        var fo = new Folk { id = e.id, name = e.name, ch = ch, act = new MotionActor(ch), L = L, p = w, yaw = (float)R.NextDouble() * 6.28f, drunk = e.name.Contains("Пьяница") };
                        fo.scene = e.name.Contains("Купец") ? "merchant" : e.name.Contains("Хозяйка") ? "hostess" : e.name.Contains("Пьяница") ? "drunkard" : null;
                        folk.Add(fo); L.colliders.Add(Collider2.Circle(w.x - L.origin.x, w.z - L.origin.z, 0.3f));
                    }
                    else if (e.type == "item" && e.name.StartsWith("Труп"))
                    {
                        var have = new List<int>();
                        if (e.name.Contains("шлем")) have.Add(0); else if (e.name.Contains("нагрудник")) { have.Add(1); have.Add(2); } else if (e.name.Contains("наголенники")) { have.Add(3); have.Add(4); } else for (int l = 0; l < 5; l++) have.Add(l);
                        L.piles.Add(NatureLib.Pile(mats, L.root.transform, w - L.origin, (float)R.NextDouble() * 360, have));
                    }
                    else if (e.type == "item") chests.Add(MakeChest(e, L, w, (float)R.NextDouble() * 360));
                }
                catch (System.Exception ex) { Debug.LogWarning("Не удалось поставить «" + e.name + "»: " + ex.Message); }
            }
        }
        Chest MakeChest(StoryMap.Ent e, StoryLocation L, Vector3 w, float yaw)
        {
            var c = new Chest { id = e.id, name = e.name, note = e.note, L = L, p = w };
            c.go = new GameObject("Chest_" + e.id); c.go.transform.SetParent(L.root.transform, false); c.go.transform.position = w; c.go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            bool small = !e.name.StartsWith("Сундук");
            float W = small ? 0.4f : 0.9f, H = small ? 0.3f : 0.45f, D = small ? 0.35f : 0.55f;
            MeshGen.Obj(MeshGen.Box(W, H, D), mats["WOOD"], c.go.transform, new Vector3(0, H / 2, 0), Quaternion.identity, true, "Box");
            if (!small) { foreach (float x in new[] { -W / 2 + 0.06f, W / 2 - 0.06f }) MeshGen.Obj(MeshGen.Box(0.05f, H + 0.02f, D + 0.02f), mats["STEEL"], c.go.transform, new Vector3(x, H / 2, 0), Quaternion.identity, true, "Band"); }
            var hinge = new GameObject("Lid"); hinge.transform.SetParent(c.go.transform, false); hinge.transform.localPosition = new Vector3(0, H, -D / 2); c.lid = hinge;
            MeshGen.Obj(MeshGen.Box(W + 0.02f, 0.08f, D + 0.02f), mats["WOOD"], hinge.transform, new Vector3(0, 0.04f, D / 2), Quaternion.identity, true, "LidBox");
            GlyphLayer.Build(c.go);
            L.colliders.Add(Collider2.Circle(w.x - L.origin.x, w.z - L.origin.z, small ? 0.25f : 0.5f));
            return c;
        }

        /// a rat-man (or the chief, a drunk, a dead knight risen)
        Foe MakeFoe(string id, string name, string kind, StoryLocation L, Vector3 w, float heading)
        {
            bool boss = kind == "boss", zombie = kind == "zombie", rat = !zombie;
            var cfgF = new CharConfig
            {
                sex = 'm', build = boss ? 3 : zombie ? 1 : 0, height = boss ? 186 : zombie ? 176 : 155,
                armor = boss ? "chain" : zombie ? (id.GetHashCode() % 2 == 0 ? "plate" : "chain") : kind == "drunk" ? "cloth" : "leather",
                weapon = boss ? "axe" : kind == "drunk" ? "none" : "sword"
            };
            var ch = Character.Build(cfgF, actorMats, name);
            var hb = ch.human;
            // the skin: grey-brown fur on the rats (near black on the chief), grey-green dead flesh on the risen
            var fur = actorMats.Tint("SKIN", boss ? 0x2c2826u : zombie ? 0x6c745au : 0x5e5650u);
            var hair = actorMats.Tint("HAIR", boss ? 0x1c1a18u : zombie ? 0x3a3a30u : 0x4a4440u);
            foreach (var r in ch.go.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials; bool ch2 = false;
                for (int i = 0; i < ms.Length; i++) { if (ms[i] == actorMats["SKIN"]) { ms[i] = fur; ch2 = true; } else if (ms[i] == actorMats["HAIR"]) { ms[i] = hair; ch2 = true; } }
                if (ch2) r.sharedMaterials = ms;
            }
            if (rat)
            {
                Transform head = hb.Bone("head"), neck = hb.Bone("neck01") ?? hb.Bone("neck");
                if (head != null && neck != null)
                {
                    var hp0 = head.position;
                    head.localScale = Vector3.one * 0.02f;          // the man's head shrinks to nothing inside the rat's
                    var rh = RatHeadObj(boss, ch.go.transform);
                    rh.transform.position = hp0 + Vector3.up * 0.02f + ch.go.transform.forward * 0.02f; rh.transform.rotation = ch.go.transform.rotation;
                    rh.transform.localScale = Vector3.one * (boss ? 1.1f : 1f);
                    rh.transform.SetParent(neck, true);
                }
                // the tail: a long bare tube from the base of the spine, curling down and back
                Transform pel = hb.Bone("pelvis") ?? hb.Bone("root") ?? hb.Bone("spine05");
                if (pel != null)
                {
                    var b0 = pel.position + ch.go.transform.forward * -0.12f; var fw = ch.go.transform.forward; var path = new List<Vector3>();
                    for (int k2 = 0; k2 <= 10; k2++) { float u = k2 / 10f; path.Add(b0 - fw * (0.75f * u * (boss ? 1.3f : 1)) + Vector3.down * (0.75f * u * u) + ch.go.transform.right * (0.18f * Mathf.Sin(u * 3.3f))); }
                    var tail = MeshGen.Obj(MeshGen.Tube(path, boss ? 0.035f : 0.026f, 6), actorMats.Tint("SKIN", 0xc89a98u), ch.go.transform, Vector3.zero, Quaternion.identity, true, "Tail");
                    tail.transform.SetParent(pel, true);
                }
                if (boss)
                {   // bone spikes on the shoulders and a jagged iron crown
                    foreach (string s in new[] { "L", "R" })
                    {
                        var sh = hb.Bone("upperarm01." + s); if (sh == null) continue;
                        for (int q = 0; q < 3; q++)
                        {
                            var sp = MeshGen.Obj(MeshGen.Cylinder(0.035f, 0.002f, 0.16f, 6), actorMats["BONE"], ch.go.transform, sh.position + Vector3.up * 0.06f + ch.go.transform.forward * (q - 1) * 0.06f, Quaternion.Euler(s == "L" ? -25 : 25, 0, (s == "L" ? 1 : -1) * 35), true, "Spike");
                            sp.transform.SetParent(sh, true);
                        }
                    }
                    Transform head2 = hb.Bone("neck01") ?? hb.Bone("neck");
                    if (head2 != null && head != null)
                    {
                        var crown = new GameObject("Crown"); crown.transform.SetParent(ch.go.transform, false); crown.transform.position = head.position + Vector3.up * 0.13f;
                        for (int q = 0; q < 7; q++) { float a = q / 7f * Mathf.PI * 2; MeshGen.Obj(MeshGen.Box(0.04f, q % 2 == 0 ? 0.09f : 0.05f, 0.015f), actorMats["STEEL"], crown.transform, new Vector3(Mathf.Sin(a) * 0.08f, 0, Mathf.Cos(a) * 0.08f), Quaternion.Euler(0, a * Mathf.Rad2Deg, 0), true, "Tooth"); }
                        crown.transform.SetParent(head2, true);
                    }
                }
            }
            ch.go.transform.SetParent(L.root.transform, true); ch.go.transform.position = w; ch.go.transform.rotation = Quaternion.Euler(0, heading * Mathf.Rad2Deg, 0);
            GlyphLayer.Build(ch.go);
            var f = new Foe { id = id, name = name, kind = kind, ch = ch, act = new MotionActor(ch), L = L, p = w, home = w, goal = w };
            f.act.heading = heading; f.act.maxSpeed = boss ? 3.4f : zombie ? 1.6f : 2.8f;
            f.maxHp = f.hp = boss ? 70 : zombie ? 40 : 30;
            f.think = Random.value * 2; f.cd = 1;
            if (kind == "drunk") { f.act.PlayCut("talk", false, true, 0.7f + 0.3f * Random.value); }
            return f;
        }
        GameObject RatHeadObj(bool boss, Transform parent)
        {
            if (ratHead == null)
            {
                var ta = Resources.Load<TextAsset>("rathead"); var b = ta.bytes; int nv = System.BitConverter.ToInt32(b, 0), nt = System.BitConverter.ToInt32(b, 4), o = 8;
                var v = new Vector3[nv]; var tag = new byte[nv];
                for (int i = 0; i < nv; i++) { v[i] = new Vector3(System.BitConverter.ToSingle(b, o), System.BitConverter.ToSingle(b, o + 4), System.BitConverter.ToSingle(b, o + 8)); tag[i] = b[o + 12]; o += 13; }
                var subs = new List<int>[4]; for (int s = 0; s < 4; s++) subs[s] = new List<int>();
                for (int i = 0; i < nt; i++)
                {
                    int a = System.BitConverter.ToInt32(b, o), c = System.BitConverter.ToInt32(b, o + 4), d = System.BitConverter.ToInt32(b, o + 8); o += 12;
                    int s = Mathf.Max(tag[a], Mathf.Max(tag[c], tag[d])); subs[s].Add(a); subs[s].Add(c); subs[s].Add(d);
                }
                ratHead = new Mesh { name = "RatHead" }; ratHead.vertices = v; ratHead.subMeshCount = 4;
                for (int s = 0; s < 4; s++) ratHead.SetTriangles(subs[s], s);
                ratHead.RecalculateNormals(); ratHead.RecalculateBounds(); MeshGen.AddGlyphUV(ratHead, false);
            }
            var go = new GameObject("RatHead"); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ratHead;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] {
                actorMats.Tint("SKIN", boss ? 0x2c2826u : 0x5e5650u),                                        // fur
                actorMats.Tint("SKIN", 0xd4949cu),                                                          // inside the ears, the nose
                boss ? actorMats.Tint("RED", 0xff2a1au, 0xc01808u) : actorMats["DARK"],                       // eyes
                actorMats["BONE"] };                                                                        // teeth
            return go;
        }

        // ---- the AI ----
        const float SEE_R = 9f, SEE_COS = 0.5f;          // 120 degrees in front, 9 m
        void UpdateFoes(float dt)
        {
            var L = loc as StoryLocation; if (L == null) return;
            var hp2 = new Vector2(pos.x, pos.z);
            for (int i = 0; i < foes.Count; i++)
            {
                var f = foes[i]; if (f.L != L) continue;
                if (f.Dead) { DeadPose(f, dt); continue; }
                f.t += dt; f.cd -= dt; f.dodgeCd -= dt; f.parryCd -= dt; f.barkCd -= dt; f.stagger -= dt; f.think -= dt; f.guardT -= dt;
                var me = new Vector2(f.p.x, f.p.z); var toH = hp2 - me; float dist = toH.magnitude;
                var fwd = new Vector2(Mathf.Sin(f.act.heading), Mathf.Cos(f.act.heading));
                bool alive = hp > 0 && respawnT <= 0;
                Vector2? dir2 = null; float mag = 0; Vector2? lockP = null;
                if (f.kind == "drunk")
                {   // they sway, mutter, turn to the hero when he stands near; never fight
                    if (dist < 3.5f) { f.act.heading = Mathf.LerpAngle(f.act.heading * Mathf.Rad2Deg, Mathf.Atan2(toH.x, toH.y) * Mathf.Rad2Deg, dt * 2) * Mathf.Deg2Rad; }
                    if (dist < 4f && !sDone.Contains("drunk_rats")) Play("drunk_rats");
                }
                else
                {
                    // seeing: in front, near enough, nothing between (walls hide him); close by he is heard
                    bool sees = alive && dist < SEE_R && (Vector2.Dot(fwd, toH / Mathf.Max(dist, 0.01f)) > SEE_COS || dist < 2.2f) && L.SightW(f.p + Vector3.up, pos + Vector3.up);
                    if (sees) { if (!f.noticed) Notice(f); f.seenT = 0; f.lastSeen = pos; } else f.seenT += dt;
                    if (f.noticed && f.seenT > (f.Boss ? 15 : 6)) { f.noticed = false; f.goal = f.lastSeen; f.state = 2; }
                    if (!alive) f.noticed = false;
                    if (f.noticed) Fight(f, dist, toH, fwd, dt, ref dir2, ref mag, ref lockP);
                    else Wander(f, dt, ref dir2, ref mag);
                }
                // the leap back / to the side
                Vector2 extra = Vector2.zero;
                if (f.leapT > 0) { f.leapT -= dt; extra = f.leapV * dt; f.leapV *= Mathf.Exp(-dt * 5); }
                if (f.stagger > 0) { dir2 = null; mag = 0; }
                var dd = f.act.Locomotion(dt, dir2, mag, lockP, me, q => FoeCollide(f, q));
                dd += f.act.TakeAttackDrift() + extra;
                var np = FoeCollide(f, me + dd);
                f.p = new Vector3(np.x, 0, np.y);
                f.ch.go.transform.position = f.p; f.ch.go.transform.rotation = Quaternion.Euler(0, f.act.heading * Mathf.Rad2Deg, 0);
                f.act.Animate(tNow, dt);
                if (f.kind != "drunk") StrikeHero(f, dist, toH);
            }
            // the hero is pushed out of the living
            foreach (var f in foes)
            {
                if (f.L != L || f.Dead) continue;
                var d = hp2 - new Vector2(f.p.x, f.p.z); float r = 0.62f * (f.Boss ? 1.2f : 1);
                if (d.magnitude < r && d.magnitude > 1e-4f) { var q = new Vector2(f.p.x, f.p.z) + d.normalized * r; var c = WCollide(q, 0.27f); pos = new Vector3(c.x, 0, c.y); hp2 = c; }
            }
        }
        Vector2 FoeCollide(Foe f, Vector2 q)
        {
            q = f.L.CollideW(q, 0.3f * (f.Boss ? 1.2f : 1));
            foreach (var o in foes)
            {
                if (o == f || o.L != f.L || o.Dead) continue;
                var d = q - new Vector2(o.p.x, o.p.z); float r = 0.6f; if (d.magnitude < r && d.magnitude > 1e-4f) q = new Vector2(o.p.x, o.p.z) + d.normalized * r;
            }
            return q;
        }
        void Notice(Foe f)
        {
            f.noticed = true; f.cd = Mathf.Max(f.cd, 0.6f);
            if (f.Boss) { if (!sDone.Contains("boss_intro")) Play("boss_intro"); }
            else if (f.barkCd <= 0) { Bark(f.ch.go.transform, f.kind == "zombie" ? "zombie_moan" : Random.value < 0.5f ? "rat_notice" : "rat_notice2"); f.barkCd = 8; }
            Sound("draw", 0.35f, f.p);
        }
        /// not after the hero: walk somewhere near home, stop, look about, rummage, go on
        void Wander(Foe f, float dt, ref Vector2? dir2, ref float mag)
        {
            var me = new Vector2(f.p.x, f.p.z); var g = new Vector2(f.goal.x, f.goal.z);
            if ((g - me).magnitude > 0.5f && f.state != 0)
            {
                var step = f.L.NextStep(f.L.ToMap(f.p), f.L.ToMap(f.goal)); var sw = f.L.ToWorld(step); var d = new Vector2(sw.x - f.p.x, sw.z - f.p.z);
                if (d.magnitude > 0.05f) { dir2 = d.normalized; mag = f.state == 2 ? 0.9f : 0.55f; }
                if (f.think < -12) f.state = 0;           // stuck: give up
                return;
            }
            if (f.state != 0) { f.state = 0; f.think = 2 + Random.value * 4; if (Random.value < 0.35f) f.act.PlayCut(Random.value < 0.5f ? "pickup" : "inspect"); }
            if (f.think <= 0)
            {   // a new place to go: within 5 m of home, on the ground, in sight of where he stands
                for (int k2 = 0; k2 < 12; k2++)
                {
                    var c = new Vector2(f.home.x, f.home.z) + Random.insideUnitCircle * (f.kind == "zombie" ? 6 : 4.5f);
                    var cm = f.L.ToMap(new Vector3(c.x, 0, c.y));
                    if (!f.L.Walk(cm)) continue;
                    f.goal = new Vector3(c.x, 0, c.y); f.state = 1; f.think = 0; break;
                }
                if (f.state == 0) f.think = 1;
            }
        }
        /// after the hero: come close, strike; the chief fights with his head
        void Fight(Foe f, float dist, Vector2 toH, Vector2 fwd, float dt, ref Vector2? dir2, ref float mag, ref Vector2? lockP)
        {
            lockP = new Vector2(pos.x, pos.z);
            float reach = (f.act.Attacking ? f.act.Reach : 1.6f) * (f.Boss ? 1.15f : 1);
            bool heroSwinging = player.Attacking && player.combatT > 0;
            // the walk to him (round the walls, if he is out of sight)
            Vector2 toward = toH;
            if (f.seenT > 0.2f) { var st = f.L.NextStep(f.L.ToMap(f.p), f.L.ToMap(f.lastSeen)); var sw = f.L.ToWorld(st); toward = new Vector2(sw.x - f.p.x, sw.z - f.p.z); }
            if (!f.Boss)
            {
                // the simple ones: walk (run when far), strike when near; now and then a leap back from his blow
                if (dist > reach * 0.85f) { dir2 = toward.normalized; mag = dist > 5 ? 1.7f : 1f; }
                else if (f.cd <= 0 && !f.act.Attacking) { f.act.StartAttack(); f.swingHit = false; f.cd = (f.kind == "zombie" ? 2.2f : 1.5f) + Random.value * 1.2f; }
                if (heroSwinging && dist < 2.4f && f.dodgeCd <= 0 && f.kind != "zombie")
                {
                    f.dodgeCd = 1.2f;
                    if (Random.value < 0.15f) Leap(f, -toH.normalized, 5.5f);
                }
                return;
            }
            // ---- the chief ----
            if (!f.raged && f.hp < f.maxHp * 0.4f) { f.raged = true; Play("boss_rage"); f.parryCd = 0; }
            float tempo = f.raged ? 0.65f : 1f;          // in his rage he is quicker to strike, slower to think of defence
            // defence first: a blow coming, he is facing it
            if (heroSwinging && dist < player.Reach + 0.4f && f.dodgeCd <= 0 && !f.act.Attacking)
            {
                f.dodgeCd = f.raged ? 1.6f : 0.9f;
                float r = Random.value;
                if (f.parryCd <= 0 && r < (f.raged ? 0.2f : 0.38f)) { f.guardT = 0.45f; f.parryCd = 2.2f; }                    // the axes up: the blow will be turned
                else if (r < (f.raged ? 0.35f : 0.68f)) { var side = new Vector2(-toH.y, toH.x).normalized * (Random.value < 0.5f ? 1 : -1); Leap(f, side * 0.8f - toH.normalized * 0.4f, 6f); }   // a step aside
                else if (r < 0.8f) Leap(f, -toH.normalized, 6.5f);                                                                // a leap back
            }
            f.act.guardHeld = f.guardT > 0;
            if (f.guardT > 0) { mag = 0; return; }
            // he reads the hero's guard: against a raised blade he circles and waits for it to drop, then a quick pair
            bool heroGuards = player.guardHeld;
            if (f.combo > 0 && !f.act.Attacking && f.cd <= 0.25f)
            {   // the next blow of a combination
                if (dist < reach * 1.3f) { f.act.StartAttack(); f.swingHit = false; f.combo--; f.cd = 0.35f * tempo; }
                else f.combo = 0;
            }
            if (dist > 4.5f) { dir2 = toward.normalized; mag = 1.8f; return; }
            if (f.act.Attacking) { if (dist > reach * 0.8f) { dir2 = toH.normalized; mag = 0.6f; } return; }
            if (heroGuards && f.cd > -1.5f)
            {   // circle him
                var side = new Vector2(-toH.y, toH.x).normalized * f.strafe;
                dir2 = (side + toH.normalized * (dist > 2.6f ? 0.5f : dist < 1.8f ? -0.4f : 0)).normalized; mag = 0.8f;
                if (Random.value < dt * 0.4f) f.strafe = -f.strafe;
                return;
            }
            if (f.cd <= 0)
            {
                if (dist < reach * 1.1f)
                {   // a combination of two or three blows, a pause of his own choosing after it
                    f.act.StartAttack(); f.swingHit = false; f.combo = Random.value < 0.55f ? 1 : Random.value < 0.6f ? 2 : 0;
                    f.cd = (1.1f + Random.value * 1.4f) * tempo;
                }
                else { dir2 = toH.normalized; mag = dist > 3 ? 1.6f : 1.1f; }      // closing in fast
            }
            else
            {   // between blows: keep at the edge of reach, drift round him
                var side = new Vector2(-toH.y, toH.x).normalized * f.strafe;
                float want = reach * 1.25f; dir2 = (side * 0.8f + toH.normalized * Mathf.Clamp(dist - want, -1, 1)).normalized; mag = 0.7f;
                if (Random.value < dt * 0.3f) f.strafe = -f.strafe;
            }
        }
        void Leap(Foe f, Vector2 d, float sp) { f.leapV = d.normalized * sp; f.leapT = 0.28f; SpawnPuff(f.p + Vector3.up * 0.3f, 0.04f, Vector3.zero, 0.4f); Sound("cloth", 0.5f, f.p); }

        /// his blow reaches the hero: a guard facing it turns it (the chief's second blow of a combination breaks through),
        /// the dash goes under it
        void StrikeHero(Foe f, float dist, Vector2 toH)
        {
            if (!f.act.Cutting || f.swingHit || hp <= 0 || respawnT > 0) return;
            float reach = f.act.Reach * (f.Boss ? 1.15f : 1);
            var fwd = new Vector2(Mathf.Sin(f.act.heading), Mathf.Cos(f.act.heading));
            if (dist > reach || Vector2.Dot(fwd, toH / Mathf.Max(dist, 0.01f)) < 0.3f) return;
            f.swingHit = true;
            if (dashT < DASH_T + 0.15f) return;           // under the dash
            var hf = new Vector2(Mathf.Sin(player.heading), Mathf.Cos(player.heading));
            bool blocked = player.guardHeld && Vector2.Dot(hf, -toH / Mathf.Max(dist, 0.01f)) > 0.2f;
            int dmg = f.kind == "zombie" ? 10 : 8;
            if (blocked)
            {
                Sound("clash", 0.7f, f.p);
                if (f.Boss && f.combo == 0 && f.cd < 0.5f) { Damage(dmg / 2); }      // the last of a combination: through the guard
                else if (!f.Boss) f.stagger = 0.5f;
                return;
            }
            Damage(dmg);
        }
        /// the hero's blade on the creatures
        void HeroHitsFoes()
        {
            var L = loc as StoryLocation; if (L == null || !player.Cutting) return;
            var hf = new Vector2(Mathf.Sin(player.heading), Mathf.Cos(player.heading));
            for (int i = 0; i < foes.Count; i++)
            {
                var f = foes[i]; if (f.L != L || f.Dead || player.atkHits.Contains(1000 + i)) continue;
                var d = new Vector2(f.p.x - pos.x, f.p.z - pos.z); float dist = d.magnitude;
                if (dist > player.Reach + (f.Boss ? 0.15f : 0) || (Vector2.Dot(hf, d / Mathf.Max(dist, 0.01f)) < 0.3f && !player.Around)) continue;
                player.atkHits.Add(1000 + i);
                if (f.leapT > 0.1f) continue;                // he is out of the way
                if (f.kind == "drunk") { f.kind = "rat"; f.act.StopCut(); }          // you hit him, he fights
                var ff = new Vector2(Mathf.Sin(f.act.heading), Mathf.Cos(f.act.heading));
                if (f.act.guardHeld && Vector2.Dot(ff, -d / Mathf.Max(dist, 0.01f)) > 0.3f)
                {   // parried: steel on steel, and he answers at once
                    Sound("clash", 0.9f, f.p); SpawnPuff((f.p + pos) / 2 + Vector3.up * 1.2f, 0.03f, Vector3.up, 0.3f);
                    f.guardT = 0; f.act.guardHeld = false; f.cd = 0; f.combo = 1; f.act.StartAttack(); f.swingHit = false;
                    player.combatT = Mathf.Max(player.combatT, 1);
                    continue;
                }
                int dmg = cfg.weapon == "none" ? 6 : 10;
                f.hp -= dmg; f.stagger = f.Boss ? 0.12f : 0.35f; Sound("hit_flesh", 0.7f, f.p);
                if (!f.noticed) Notice(f);
                f.lastSeen = pos; f.seenT = 0;
                if (!f.Boss && !f.hurtBark && f.kind == "rat" && Random.value < 0.6f) { f.hurtBark = true; Bark(f.ch.go.transform, "rat_hurt"); }
                if (f.Dead) Die(f);
            }
        }
        void Die(Foe f)
        {
            f.deadT = 0; f.act.StopCut(); SpawnPuff(f.p + Vector3.up * 0.8f, 0.06f, Vector3.up * 0.4f, 0.8f);
            Sound("plates", 0.6f, f.p);
            if (f.Boss) Play("boss_dead");
        }
        /// he falls (backwards, with a twist) and stays where he fell
        void DeadPose(Foe f, float dt)
        {
            if (f.deadT > 1) return;
            f.deadT += dt; float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(f.deadT / 0.55f));
            var tr = f.ch.go.transform;
            tr.rotation = Quaternion.Euler(0, f.act.heading * Mathf.Rad2Deg + 20 * u, 0) * Quaternion.Euler(-88 * u, 0, 0);
            tr.position = f.p + Vector3.up * (0.12f * u) - tr.forward * 0;
            f.act.Locomotion(dt, null, 0, null, new Vector2(f.p.x, f.p.z), null); f.act.Animate(tNow, dt);
        }
        Vector2? StoryNearest(Vector2? best, ref float bd)
        {
            var L = loc as StoryLocation; if (L == null) return best;
            foreach (var f in foes)
            {
                if (f.L != L || f.Dead || f.kind == "drunk") continue;
                float d = new Vector2(f.p.x - pos.x, f.p.z - pos.z).magnitude; if (d < bd) { bd = d; best = new Vector2(f.p.x, f.p.z); }
            }
            return best;
        }

        void UpdateFolk(float dt)
        {
            var L = loc as StoryLocation; if (L == null) return;
            foreach (var p in folk)
            {
                if (p.L != L) continue;
                var toH = new Vector2(pos.x - p.p.x, pos.z - p.p.z); float d = toH.magnitude;
                float want = p.faceT > 0 || d < 3 ? Mathf.Atan2(toH.x, toH.y) : p.yaw; p.faceT -= dt;
                p.act.heading = Mathf.LerpAngle(p.act.heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg, dt * 3) * Mathf.Deg2Rad;
                p.act.Locomotion(dt, null, 0, null, new Vector2(p.p.x, p.p.z), null); p.act.noLock = true;
                p.ch.go.transform.rotation = Quaternion.Euler(0, p.act.heading * Mathf.Rad2Deg, 0);
                p.act.Animate(tNow, dt);
                p.barkT -= dt;
                if (p.scene != null && d < 2.4f && p.barkT <= 0 && sDir == null) { p.barkT = 25; Bark(p.ch.go.transform, p.scene); }
            }
        }
        void UpdateChests(float dt)
        {
            var L = loc as StoryLocation; if (L == null) return;
            foreach (var c in chests)
            {
                if (c.L != L) continue;
                if (c.open) { c.openT = Mathf.Min(1, c.openT + dt * 2.5f); c.lid.transform.localRotation = Quaternion.Euler(-100 * Mathf.SmoothStep(0, 1, c.openT), 0, 0); continue; }
                if (new Vector2(c.p.x - pos.x, c.p.z - pos.z).magnitude > 1.3f || hp <= 0) continue;
                c.open = true; Sound("door_open", 0.5f, c.p);
                if (c.name.Contains("оружием")) { Play("weapon_chest"); if (cfg.weapon == "none") SetWeapon("sword"); inventory.Add("Записка о Большой Крысе"); }
                else if (c.name.Contains("Зелья")) { RestoreBody(); ShowToast("Зелья: доспех цел, целостность 100%"); }
                else if (c.name.Contains("шлемом")) { inventory.Add("Шлем крысолюдов"); ShowToast("Найден шлем крысолюдов"); }
                else if (c.name.Contains("драгоценност")) { inventory.Add("Драгоценности"); ShowToast("Драгоценности"); }
                else if (c.name.Contains("алкогол")) { inventory.Add("Бутыль крысиного пойла"); ShowToast("Бутыль крысиного пойла"); }
                else { inventory.Add("Вещица из трактира"); ShowToast("Подобрано: вещица"); }
            }
        }
        void UpdateSkull(float dt)
        {
            if (skullGo == null || !skullGo.activeSelf) return;
            skullYaw = Mathf.MoveTowardsAngle(skullYaw, skullYawT, dt * 70);
            skullGo.transform.rotation = Quaternion.Euler(0, skullYaw, 0);
        }
        bool NearSkull() { return skullGo != null && skullGo.activeSelf && loc == skullL && new Vector2(skullGo.transform.position.x - pos.x, skullGo.transform.position.z - pos.z).magnitude < 1.6f; }
        void TakeSkull()
        {
            if (sHasSkull) return;
            sHasSkull = true; if (skullGo != null) skullGo.SetActive(false);
            inventory.Add("Череп Ёрика"); ShowToast("Череп Ёрика теперь с тобой"); Sound("cloth", 0.5f, pos);
        }

        /// the trigger points of the map: a scene plays when the hero comes near (once)
        void StoryTriggers()
        {
            var L = loc as StoryLocation; if (L == null) return;
            var m = L.ToMap(pos);
            foreach (var e in smap.ents)
            {
                if (e.type != "trigger" || SLocAt(e.p) != L || (m - e.p).magnitude > 4.5f) continue;
                string s = e.name.Contains("черепом") ? "skull" : e.name.Contains("выбор") ? "choice" : e.name.Contains("трактире") ? "tavern_argument" : null;
                if (s != null && !sDone.Contains(s)) Play(s);
            }
            // the corpses: a hint the first time he is near one and has a piece missing
            if (!sDone.Contains("corpse_hint") && LimbCount() < 5)
                foreach (var p in L.piles) if (p.Count > 0 && (new Vector2(pos.x - p.pos.x - L.origin.x, pos.z - p.pos.z - L.origin.z)).magnitude < 3.5f) { Bark(hero.go.transform, "corpse_hint"); break; }
            // the arena: the chief sees the hero come in
            foreach (var c in smap.circles) if ((m - c.c).magnitude < c.r - 1.5f) foreach (var f in foes) if (f.Boss && !f.Dead && !f.noticed && f.L == L) Notice(f);
        }

        // ================= scenes =================
        class DLine { public int branch; public string who, text, act; public string[] choice; }
        class DScene { public string id, title; public int tier; public bool gameLeads; public readonly List<DLine> lines = new List<DLine>(); }
        readonly Dictionary<string, DScene> scenes = new Dictionary<string, DScene>();
        void LoadScenes()
        {
            scenes.Clear();
            var ta = Resources.Load<TextAsset>("Story/dialogues"); if (ta == null) { Debug.LogWarning("Нет диалогов"); return; }
            DScene cur = null;
            foreach (var raw in ta.text.Split('\n'))
            {
                var l = raw.TrimEnd('\r').Trim(); if (l.Length == 0 || l.StartsWith("#")) continue;
                if (l.StartsWith("==="))
                {
                    var p = l.Substring(3).Split('|');
                    cur = new DScene { id = p[0].Trim(), tier = p.Length > 1 ? (int)char.GetNumericValue(p[1].Trim()[1]) : 1, gameLeads = p.Length > 2 && p[2].Trim() == "B", title = p.Length > 3 ? p[3].Trim() : "" };
                    scenes[cur.id] = cur; continue;
                }
                if (cur == null) continue;
                var dl = new DLine();
                if (l.StartsWith("[")) { int e = l.IndexOf(']'); int.TryParse(l.Substring(1, e - 1), out dl.branch); l = l.Substring(e + 1).Trim(); }
                if (l.StartsWith("@")) dl.act = l.Substring(1).Trim();
                else if (l.StartsWith("?")) { var p = l.Substring(1).Split('|'); dl.choice = new string[p.Length]; for (int i = 0; i < p.Length; i++) dl.choice[i] = p[i].Trim(); }
                else { int c = l.IndexOf(':'); if (c > 0 && c < 40) { dl.who = l.Substring(0, c).Trim(); dl.text = l.Substring(c + 1).Trim(); } else dl.text = l; }
                cur.lines.Add(dl);
            }
        }
        // the director: one scene of T1-T3 at a time (the rest wait their turn); T0 lines over heads, any number
        class Running { public DScene s; public int ix = -1, chosen, tier; public float lineT, dur; public DLine line; public bool choosing, waitCut; public float choiceT; }
        Running sDir; readonly Queue<string> queue = new Queue<string>();
        class BarkLine { public Transform at; public string who, text; public float t, dur; }
        readonly List<BarkLine> barks = new List<BarkLine>();
        float letter;      // the black bars of a full scene, 0..1

        void Play(string id)
        {
            DScene s; if (!scenes.TryGetValue(id, out s) || sDone.Contains(id)) return;
            if (s.tier == 0) { sDone.Add(id); return; }
            sDone.Add(id);
            if (sDir != null) { queue.Enqueue(id); return; }
            sDir = new Running { s = s, tier = s.tier }; Next();
        }
        void Bark(Transform at, string id)
        {
            DScene s; if (!scenes.TryGetValue(id, out s)) return;
            foreach (var l in s.lines) if (l.text != null) { barks.Add(new BarkLine { at = at, who = l.who, text = l.text, dur = 1.6f + 0.05f * l.text.Length }); break; }
            if (id == "corpse_hint") sDone.Add(id);
        }
        void Next()
        {
            while (true)
            {
                sDir.ix++;
                if (sDir.ix >= sDir.s.lines.Count) { End(); return; }
                var l = sDir.s.lines[sDir.ix];
                if (l.branch != 0 && l.branch != sDir.chosen) continue;
                if (l.act != null)
                {   // a clip waits for the one before it to end (the hero gets up before he looks at his hands)
                    if (l.act.StartsWith("cut") && player.InCut && !player.CutHeld && player.CutProgress < 0.97f) { sDir.ix--; sDir.waitCut = true; return; }
                    Act(l.act); continue;
                }
                sDir.line = l; sDir.lineT = 0;
                if (l.choice != null) { sDir.choosing = true; sDir.choiceT = 0; sDir.dur = 6; return; }
                sDir.dur = 1.4f + 0.055f * l.text.Length * (sDir.tier >= 2 ? 1.1f : 1);
                return;
            }
        }
        void End()
        {
            string id = sDir.s.id; sDir = null;
            if (id == "wake") { letter = Mathf.Min(letter, 1); Play("letter"); }
            if (queue.Count > 0) { var n = queue.Dequeue(); sDone.Remove(n); Play(n); }
        }
        void Act(string a)
        {
            var p = a.Split(' ');
            switch (p[0])
            {
                case "letterbox": letterOn = p.Length > 1 && p[1] == "on"; break;
                case "cut": if (p.Length > 1) player.PlayCut(p[1], p.Length > 2 && p[2] == "hold"); break;
                case "damage": { int n; if (p.Length > 1 && int.TryParse(p[1], out n)) Damage(n); break; }
                case "take": if (p.Length > 1 && p[1] == "skull") TakeSkull(); break;
                case "face":
                    if (sDir != null && sDir.s.id == "skull" && skullGo != null) { var d = pos - skullGo.transform.position; skullYawT = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
                    else foreach (var f in folk) if (f.L == loc) f.faceT = 30;
                    break;
                case "flag": if (p.Length > 1) sFlags.Add(p[1]); break;
            }
        }
        bool letterOn;
        void SkipScene()
        {
            if (sDir == null) return;
            // the rest of the scene happens at once (its damage, its flags), without the words
            for (int i = sDir.ix + 1; i < sDir.s.lines.Count; i++) { var l = sDir.s.lines[i]; if (l.act != null && (l.branch == 0 || l.branch == sDir.chosen) && !l.act.StartsWith("cut")) Act(l.act); }
            player.StopCut(); letterOn = false; End();
        }
        void UpdateDirector(float dt)
        {
            letter = Mathf.MoveTowards(letter, letterOn && sDir != null ? 1 : 0, dt / 0.5f);
            for (int i = barks.Count - 1; i >= 0; i--) { barks[i].t += dt; if (barks[i].t > barks[i].dur || barks[i].at == null) barks.RemoveAt(i); }
            if (sDir == null) return;
            sDir.lineT += dt;
            if (sDir.waitCut) { if (!player.InCut || player.CutProgress >= 0.97f) { sDir.waitCut = false; Next(); } return; }
            if (sDir.choosing) { if (sDir.lineT > sDir.dur) Choose(1); return; }
            if (sDir.lineT > sDir.dur) Next();
        }
        void Choose(int n) { if (sDir == null || !sDir.choosing) return; sDir.chosen = n; sDir.choosing = false; Next(); }
        /// a full scene takes the hero; the guided one slows him to a walk
        bool StoryLock() { return story && world && sDir != null && sDir.tier >= 3; }
        float StoryCapMag(float mag) { return story && sDir != null && sDir.tier == 2 ? Mathf.Min(mag, 1f) : mag; }

        /// once a frame in the world, after the hero has moved
        void StoryUpdate(float dt)
        {
            if (!story || !(loc is StoryLocation)) return;
            StoryLight(false);
            HeroHitsFoes(); UpdateFoes(dt); UpdateFolk(dt); UpdateChests(dt); UpdateSkull(dt);
            StoryTriggers(); StoryPassages(dt); UpdateDirector(dt);
            hero.go.transform.position = pos + Vector3.down * heroSink;
        }

        // ---- on the screen ----
        GUIStyle sSub, sWho, sBark;
        void StoryGUI(float W, float H)
        {
            if (!story) return;
            if (sSub == null || sSub.fontSize != Mathf.RoundToInt(15 * k))
            {
                sSub = new GUIStyle(sSmall) { fontSize = Mathf.RoundToInt(15 * k), wordWrap = true, alignment = TextAnchor.UpperCenter, richText = false }; sSub.normal.textColor = new Color(0.92f, 0.9f, 0.86f);
                sWho = new GUIStyle(sSub) { fontSize = Mathf.RoundToInt(12 * k) }; sWho.normal.textColor = new Color(0.78f, 0.55f, 1f);
                sBark = new GUIStyle(sSub) { fontSize = Mathf.RoundToInt(12 * k), alignment = TextAnchor.LowerCenter }; sBark.normal.textColor = new Color(1f, 0.92f, 0.7f);
            }
            var vr = ViewRect();
            // the black bars
            if (letter > 0) { float bh = H * 0.11f * Mathf.SmoothStep(0, 1, letter); Fill(new Rect(0, 0, W, bh), Color.black); Fill(new Rect(0, H - bh, W, bh), Color.black); }
            // lines over heads
            foreach (var b in barks)
            {
                if (b.at == null || !b.at.gameObject.activeInHierarchy) continue;
                var v = sceneCam.WorldToViewportPoint(b.at.position + Vector3.up * 2.15f); if (v.z < 0) continue;
                float a = Mathf.Clamp01(b.t / 0.2f) * Mathf.Clamp01((b.dur - b.t) / 0.4f);
                var r = new Rect(vr.x + v.x * vr.width - 130 * k, vr.y + (1 - v.y) * vr.height - 54 * k, 260 * k, 50 * k);
                var old = GUI.color; GUI.color = new Color(1, 1, 1, a); GUI.Label(r, b.text, sBark); GUI.color = old;
            }
            // the skull lies near: take it
            if (!sHasSkull && NearSkull() && (sDir == null || sDir.s.id != "skull"))
                if (Button(new Rect(W / 2 - 80 * k, H * 0.62f, 160 * k, 34 * k), "ВЗЯТЬ ЧЕРЕП")) TakeSkull();
            if (sDir == null || sDir.line == null) return;
            // subtitles: who, what; a tap moves on (in the guided and full scenes)
            var L2 = sDir.line; float y = H - (letter > 0.5f ? H * 0.11f : 0) - 92 * k;
            var box = new Rect(W / 2 - 300 * k, y, 600 * k, 78 * k);
            if (!sDir.choosing)
            {
                Smoke(box, false, 0, 0.85f);
                if (!string.IsNullOrEmpty(L2.who)) GUI.Label(new Rect(box.x, box.y + 2 * k, box.width, 18 * k), L2.who, sWho);
                int shown = Mathf.Min(L2.text.Length, Mathf.FloorToInt(sDir.lineT * 45));
                GUI.Label(new Rect(box.x + 10 * k, box.y + 20 * k, box.width - 20 * k, box.height - 20 * k), L2.text.Substring(0, shown), sSub);
                if (sDir.tier >= 2 && Tapped(new Rect(0, 0, W, H))) { if (shown < L2.text.Length) sDir.lineT = L2.text.Length / 45f; else Next(); }
            }
            else
            {   // a choice: the question, the answers; the time runs out to the first one
                var q = L2.choice; int n = q.Length - 1; float bw = Mathf.Min(260 * k, (W - 60 * k) / Mathf.Max(1, n) - 10 * k);
                GUI.Label(new Rect(box.x, box.y - 6 * k, box.width, 20 * k), q[0], sWho);
                for (int i = 1; i <= n; i++)
                    if (Button(new Rect(W / 2 - (n * (bw + 10 * k)) / 2 + (i - 1) * (bw + 10 * k), box.y + 18 * k, bw, 38 * k), q[i], i == 1)) { Choose(i); return; }
                float left = 1 - sDir.lineT / sDir.dur; Fill(new Rect(W / 2 - 120 * k, box.y + 62 * k, 240 * k * left, 3 * k), new Color(0.78f, 0.55f, 1f, 0.8f));
            }
            if (sDir != null && sDir.tier >= 3 && Button(new Rect(W - safe.x - 150 * k, safe.y + 10 * k, 136 * k, 28 * k), "ПРОПУСТИТЬ", false, sSmallBtn())) SkipScene();
        }
        // the skull in the pocket: a little cloud of its own by the hero, the skull in characters, its eyes on the hero
        static readonly string[][] SKULL = {
            new[] { "  .-\"\"\"-.  ", " /       \\ ", "|  (o) (o) |", "|    ^    |", " \\  ===  / ", "  '-...-'  " },
            new[] { "  .-\"\"\"-.  ", " /       \\ ", "| (o) (o)  |", "|    ^    |", " \\  ===  / ", "  '-...-'  " },
            new[] { "  .-\"\"\"-.  ", " /       \\ ", "|  (-) (-) |", "|    ^    |", " \\  ===  / ", "  '-...-'  " },
        };
        GUIStyle sSkull;
        void PocketSkull(Rect lp, float alpha)
        {
            if (!sHasSkull || alpha < 0.01f) return;
            float r = lp.width * 0.26f; var c = new Vector2(lp.x - r * 0.15f, lp.y + r * 0.9f);
            AddPiece(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), r, false, 0, alpha);
            int fs = Mathf.Max(6, Mathf.RoundToInt(r * 0.21f));
            if (sSkull == null || sSkull.fontSize != fs) { sSkull = new GUIStyle(sMono) { fontSize = fs, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = false }; sSkull.normal.textColor = new Color(0.86f, 0.82f, 0.7f); }
            float t = Time.unscaledTime; int f = (t % 4.3f) < 0.15f ? 2 : (Mathf.FloorToInt(t / 2.6f) % 2);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
            GUI.Label(new Rect(c.x - r, c.y - r * 0.75f, 2 * r, r * 1.5f), string.Join("\n", SKULL[f]), sSkull);
            GUI.Label(new Rect(c.x - r, c.y + r * 0.62f, 2 * r, r * 0.4f), "Ёрик", sSmall);
            GUI.color = old;
        }
        string StoryTitle() { var L = loc as StoryLocation; return L != null ? L.TitleAt(pos) : null; }
    }
}
