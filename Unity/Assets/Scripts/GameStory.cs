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
            // the places: every run of tiles of one kind joined side to side - inns (tavern), caves (cave, the round
            // arenas with them), the open land (everything else)
            string Kind(string b) { return b == "tavern" ? "inn" : b == "cave" || b == "crystal" ? "cave" : "out"; }
            var tileAt = new Dictionary<long, StoryMap.Tile>(); foreach (var t in smap.tiles) tileAt[StoryLocation.Key(t.i, t.j)] = t;
            regionOf.Clear(); regionTiles.Clear(); regionKind.Clear();
            int nIn = 0, nCave = 0, nOut = 0;
            foreach (var kv in tileAt)
            {
                if (regionOf.ContainsKey(kv.Key)) continue;
                string kind = Kind(kv.Value.b); string name = kind == "inn" ? "inn" + nIn++ : kind == "cave" ? "cave" + nCave++ : "out" + nOut++;
                var set = new HashSet<long>(); var q = new Queue<StoryMap.Tile>(); q.Enqueue(kv.Value); regionOf[kv.Key] = name;
                while (q.Count > 0)
                {
                    var t = q.Dequeue(); set.Add(StoryLocation.Key(t.i, t.j));
                    foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                    {
                        long nk = StoryLocation.Key(t.i + d.x, t.j + d.y); StoryMap.Tile nt;
                        if (!regionOf.ContainsKey(nk) && tileAt.TryGetValue(nk, out nt) && Kind(nt.b) == kind) { regionOf[nk] = name; q.Enqueue(nt); }
                    }
                }
                regionTiles[name] = set; regionKind[name] = kind;
            }
            // the ways between: for every two places that touch, the middle of their shared edges (one with no wall
            // drawn across it). Inns keep their own door inside (the test world's tavern): its point is the door.
            sPass.Clear();
            var pairs = new Dictionary<string, List<KeyValuePair<Vector2, Vector2>>>();
            foreach (var kv in tileAt)
                foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(0, 1) })
                {
                    long nk = StoryLocation.Key(kv.Value.i + d.x, kv.Value.j + d.y); if (!tileAt.ContainsKey(nk)) continue;
                    string ra = regionOf[kv.Key], rb = regionOf[nk]; if (ra == rb) continue;
                    var c = new Vector2((kv.Value.i + 0.5f) * smap.tile, (kv.Value.j + 0.5f) * smap.tile) + new Vector2(d.x, d.y) * smap.tile * 0.5f;
                    string key = string.CompareOrdinal(ra, rb) < 0 ? ra + "|" + rb : rb + "|" + ra; var n = new Vector2(d.x, d.y); if (string.CompareOrdinal(ra, rb) > 0) n = -n;
                    List<KeyValuePair<Vector2, Vector2>> l; if (!pairs.TryGetValue(key, out l)) { l = new List<KeyValuePair<Vector2, Vector2>>(); pairs[key] = l; }
                    l.Add(new KeyValuePair<Vector2, Vector2>(c, n));          // n: out of the first place of the pair
                }
            var innDoor = new Vector2(-3.7f, -3.6f); var innOut = new Vector2(0, -1);       // the tavern's door (map = mirrored local), the way out
            foreach (var kv in pairs)
            {
                var ab = kv.Key.Split('|'); var edges = kv.Value;
                Vector2 mid = Vector2.zero; foreach (var e in edges) mid += e.Key; mid /= edges.Count;
                int best = 0; float bs = 1e9f;
                for (int i = 0; i < edges.Count; i++)
                {
                    float sc = (edges[i].Key - mid).magnitude;
                    foreach (var w in smap.walls) if (SegDist(edges[i].Key, w.a, w.b) < 0.8f) sc += 50;
                    if (sc < bs) { bs = sc; best = i; }
                }
                string ka = regionKind[ab[0]], kb = regionKind[ab[1]];
                var g = new StoryPassage { from = ab[0], to = ab[1], pA = edges[best].Key, nA = edges[best].Value, pB = edges[best].Key, nB = -edges[best].Value };
                string Look(string mine, string other) { return mine == "inn" ? "inn" : mine == "cave" ? (other == "out" ? "hole" : "hole") : other == "inn" ? "house" : other == "cave" ? "mouth" : "door"; }
                g.styleA = Look(ka, kb); g.styleB = Look(kb, ka);
                if (ka == "inn") { g.pA = innDoor; g.nA = innOut; }
                if (kb == "inn") { g.pB = innDoor; g.nB = innOut; }
                sPass.Add(g);
            }
            // a round arena belongs to the place whose tiles it touches (its centre may lie where no tile is)
            circleRegion.Clear();
            foreach (var c in smap.circles)
            {
                string best = null; float bd = 1e9f;
                foreach (var kv in tileAt)
                {
                    var tc = new Vector2((kv.Value.i + 0.5f) * smap.tile, (kv.Value.j + 0.5f) * smap.tile); float d = (tc - c.c).magnitude;
                    if (d < c.r + smap.tile && d < bd && (Kind(kv.Value.b) == "cave" || Kind(c.b) != "cave")) { bd = d; best = regionOf[kv.Key]; }
                }
                circleRegion.Add(best);
            }
            // the places, far from the test world and from each other
            var caveTiles = new HashSet<long>(); foreach (var kv in regionKind) if (kv.Value == "cave") caveTiles.UnionWith(regionTiles[kv.Key]);
            sLocs.Clear(); int k2 = 0;
            foreach (var kv in regionKind)
            {
                var at = STORY_AT + new Vector3(500 * k2, 0, 0); k2++;
                StoryLocation L;
                if (kv.Value == "inn") L = StoryLocation.BuildInn(mats, smap, kv.Key, sPass, at, 20 + k2);
                else
                {
                    bool circles = circleRegion.Contains(kv.Key);
                    L = StoryLocation.Build(mats, smap, kv.Key, regionTiles[kv.Key], circles, sPass, at, 11 + k2, kv.Value == "out", kv.Value == "out" ? caveTiles : null);
                }
                sLocs.Add(L);
            }
            sMain = null; foreach (var l in sLocs) if (l.outdoor && sMain == null) sMain = l;
            foreach (var l in sLocs)
            {
                string kind = regionKind[l.region];
                l.title = kind == "cave" ? "Пещеры" : kind == "out" ? "Поляна" : "Трактир";
                if (kind == "inn") foreach (var g in l.passages) l.title = regionKind[g.Other(l.region)] == "cave" ? "Крысиный трактир" : "Трактир «Пьяный дракон»";
                if (kind == "out") { l.ambSky = Mats.Hex(0x8fa4d8) * 0.55f; l.ambEq = Mats.Hex(0x4a5a58) * 0.55f; l.ambGround = Mats.Hex(0x1a2414) * 0.55f; l.moonK = 1; l.heroLightK = 1.6f; l.exposure = 1.3f; }
                else { l.ambSky = Mats.Hex(0x40506a) * 0.16f; l.ambEq = Mats.Hex(0x303840) * 0.14f; l.ambGround = Mats.Hex(0x181410) * 0.12f; l.moonK = 0; l.heroLightK = 1.05f; l.exposure = 1.3f; }
                if (kind == "inn") { l.ambSky = Mats.Hex(0x50465a) * 0.45f; l.ambEq = Mats.Hex(0x342b32) * 0.45f; l.ambGround = Mats.Hex(0x18100a) * 0.45f; l.heroLightK = 1f; }
                GlyphLayer.Build(l.root);
                CollectGlows(l.root);
                var LL = l; l.addRipple = (x, z, a) => AddRipple(x + LL.origin.x, z + LL.origin.z, a);
                l.onDrip = (x, z) => Sound("drip", 0.5f, new Vector3(x + LL.origin.x, 0, z + LL.origin.z));
            }
            LoadScenes();
            SpawnStoryThings();          // (while the places are on: the characters of what is switched off are not laid out)
            foreach (var l in sLocs) l.SetActive(false);
        }
        readonly Dictionary<long, string> regionOf = new Dictionary<long, string>(); readonly List<string> circleRegion = new List<string>();
        readonly Dictionary<string, HashSet<long>> regionTiles = new Dictionary<string, HashSet<long>>();
        readonly Dictionary<string, string> regionKind = new Dictionary<string, string>();
        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)); return (a + ab * t - p).magnitude;
        }
        /// which place a point of the map belongs to
        StoryLocation SLocAt(Vector2 m)
        {
            string r = null; long key = StoryLocation.Key(Mathf.FloorToInt(m.x / smap.tile), Mathf.FloorToInt(m.y / smap.tile));
            for (int i = 0; i < smap.circles.Count; i++) if ((m - smap.circles[i].c).magnitude < smap.circles[i].r && circleRegion[i] != null) { r = circleRegion[i]; break; }
            if (r == null) regionOf.TryGetValue(key, out r);
            foreach (var l in sLocs) if (l.region == r) return l;
            return sMain;
        }
        /// where a point of the map stands in its place (in an inn: the same spot of the room, scaled into the tavern)
        Vector3 SWorld(StoryLocation L, Vector2 m)
        {
            if (L.inn == null) return L.ToWorld(m);
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            foreach (var key in regionTiles[L.region]) { int i = (int)(key >> 32), j = (int)(key & 0xffffffff); x0 = Mathf.Min(x0, i * smap.tile); y0 = Mathf.Min(y0, j * smap.tile); x1 = Mathf.Max(x1, (i + 1) * smap.tile); y1 = Mathf.Max(y1, (j + 1) * smap.tile); }
            float u = Mathf.InverseLerp(x0, x1, m.x), v = Mathf.InverseLerp(y0, y1, m.y);
            var b = L.bounds; var lp = new Vector2(Mathf.Lerp(b.xMax - 1f, b.xMin + 1f, u), Mathf.Lerp(b.yMin + 1f, b.yMax - 1f, v));
            lp = L.Collide(lp, 0.4f);
            return new Vector3(lp.x, 0, lp.y) + L.origin;
        }
        /// the new game (or the game going on, if one is): the hero wakes in the dungeon
        void StartStory(bool fresh)
        {
            try { if (tavern == null) SetupWorld(); if (smap == null) BuildStory(); }
            catch (System.Exception e) { Debug.LogException(e); ShowToast("Не удалось собрать локацию"); return; }
            if (fresh && sDone.Count > 0) { ResetStory(); }
            bool first = !sDone.Contains("wake");
            var hl = sMain; Vector3 at = sMain.SpawnW;
            foreach (var e in smap.ents) if (e.type == "hero") { hl = SLocAt(e.p); at = SWorld(hl, e.p); hl.spawn = at - hl.origin; }
            if (!first && sLast != null) { StoryGo(sLast, sLastPos, sLastHeading); return; }
            if (first) { RestoreBody(); if (player != null) player.SetLimbs(limbs); }
            StoryGo(hl, at, Mathf.PI * 0.5f);
            if (first) { player.PlayCut("wake", true); Play("wake"); }
        }
        StoryLocation sLast; Vector3 sLastPos; float sLastHeading; float visT;
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
            L.focus = pos - L.origin; L.CullNow(); visT = 0;
            StoryLight(true);
            ShowToast(L.TitleAt(pos));
        }
        /// the light follows the ground under the hero: dark caves (his own glow), the night meadow, the warm inn
        // ---- the clock: the game starts in the evening of the first day of summer ----
        float gameClock = Sky.DAY_S * 0.585f;
        public int GameDay => Mathf.FloorToInt(gameClock / Sky.DAY_S);
        void StoryClock(float dt)
        {
            gameClock += dt;
            float d = gameClock / Sky.DAY_S, ph = d - Mathf.Floor(d);
            // morning to evening takes 16 of the day's 24 minutes: stretch the light part, squeeze the night
            float lightFrac = Sky.LIGHT_S / Sky.DAY_S;
            float keyPh = ph < lightFrac ? ph / lightFrac * 0.667f : 0.667f + (ph - lightFrac) / (1 - lightFrac) * 0.333f;
            skyS.SetClock(keyPh);
            skyS.season = (1 + GameDay / Sky.SEASON_DAYS) % 4;
        }
        /// the light follows the place: outside the sky (its clock, its season, its weather), the caves dark (the hero's
        /// own glow), the inns warm
        void StoryLight(bool force)
        {
            var L = loc as StoryLocation; if (L == null) return;
            StoryClock(Time.deltaTime);
            if (L.outdoor)
            {
                skyS.Update(Time.deltaTime);
                Color atn = skyS.ambTint * skyS.ambK;
                SetAmbient(L.ambSky * atn, L.ambEq * atn, L.ambGround * atn);
                if (moonLight != null) { moonLight.intensity = skyS.sunK * L.moonK; moonLight.enabled = moonLight.intensity > 0.01f; moonLight.color = skyS.sunCol; moonLight.transform.rotation = Quaternion.LookRotation(-skyS.sunDir); }
                heroLight.intensity = L.heroLightK * skyS.heroK; ascii.exposure = L.exposure * skyS.expK;
                float amt = skyS.Rain + skyS.Snow;
                if (precipGo == null && amt > 0.01f) precipGo = GlyphLayer.MakePrecip(4000);
                if (precipGo != null)
                {
                    if (precipGo.activeSelf != amt > 0.01f) precipGo.SetActive(amt > 0.01f);
                    Shader.SetGlobalVector("_AsciiPrecipW", new Vector4(skyS.Rain, skyS.Snow, skyS.DayK, 0));
                    Shader.SetGlobalVector("_PrecipBox", new Vector4(camTgt.x, 0, camTgt.z, 34));
                }
                sBiome = "out";
                return;
            }
            string b = L.inn != null ? "tavern" : "cave";
            if (!force && b == sBiome) return;
            sBiome = b;
            Sky.Neutral(); ascii.exposure = L.exposure;
            if (moonLight != null) moonLight.enabled = false;
            heroLight.intensity = 1.6f * L.heroLightK; SetAmbient(L.ambSky, L.ambEq, L.ambGround);
            if (precipGo != null) precipGo.SetActive(false);
        }
        void StoryHide()
        {
            Shader.SetGlobalFloat("_VisOn", 0);
            if (skyS != null) skyS.clock = false;
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
                var gp = g.P(L.region); var nOut = g.N(L.region);
                if ((m - gp).magnitude > (L.inn != null ? 0.75f : 1.05f)) continue;
                var v = player.Velocity; var vm = new Vector2(-v.x, v.z);
                if (vm.sqrMagnitude > 0.01f && Vector2.Dot(vm.normalized, nOut) < 0.2f) continue;
                var other = SLoc(g.Other(L.region)); if (other == null) continue;
                passCd = 1.5f;
                var op = g.P(other.region); var on = g.N(other.region);
                var into = -on;               // into the other place
                var target = other.ToWorld(op + into * 1.3f); float hd = Mathf.Atan2(-into.x, into.y);
                string st = g.Style(L.region);
                Reprint(() => { StoryGo(other, target, hd); autoT = 0; autoAfterScene = true; });          // (a new place: saved as he comes in)
                Sound(st == "hole" || st == "mouth" ? "hit_stone" : "door_open", 0.6f, pos);
                return;
            }
        }

        // ================= the people and the creatures =================
        class Foe
        {
            public string id, name, kind; public Character ch; public MotionActor act; public StoryLocation L;
            public Vector3 p, home, goal; public int hp, maxHp; public float scale = 1;
            public int state; public float t, cd, seenT, lostT, dodgeCd, parryCd, leapT, stagger, deadT, barkCd, guardT, strafe = 1, dmgK = 1, think, biteT = -1, biteCd = 2; public bool fortitudeUsed;
            public Vector2 leapV, step; public float stepT; public bool noticed, hurtBark, swingHit, raged; public int combo; public Vector3 lastSeen;
            public BoneArm bones; public bool gone; public Vector3 dFrom, dTo, triB; public float dropY; public Vector2 runDir; public float dDur, dT, trailT, seqT, triCd, summonCd = 8; public int seq; public bool grabber; public Foe grabbed; public float fourK, fourPh, retreatT, throwCd = 3, windT = -1; public bool parryRiposte; public Quaternion[] hunchRest, hunchPre, hunchSet; public bool hunchOn;
            // the rat's body language: crouch, paws, the nervous head, the tail (a sprung chain of bones)
            public float crouch, pawK, washK, curlK, jerk, lookY, lookT, fidT, lastStag; public int fid; public Vector3[] feetA = new Vector3[2];
            public Transform[] tailB; public Vector3[] tailP, tailQ, tailRest; public Transform tailRoot; public Vector3 tailOff; public float tailSeg;
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
                var L = SLocAt(e.p); var w = SWorld(L, e.p);
                if (e.type == "enemy" && e.name.Contains("Босс")) foreach (var c in smap.circles) if ((e.p - c.c).magnitude < c.r) w = L.ToWorld(c.c);      // the chief stands in the middle of his arena
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
                        Vector3 heroAt = w; foreach (var h in smap.ents) if (h.type == "hero") heroAt = SWorld(L, h.p);
                        var d = heroAt - w; skullYaw = skullYawT = Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;
                    }
                    else if (e.type == "npc")
                    {
                        bool drunk = e.name.Contains("крысолюд");
                        var f = drunk ? MakeFoe(e.id, e.name, "drunk", L, w, (float)R.NextDouble() * 6.28f) : null;
                        if (drunk) { foes.Add(f); continue; }
                        var cfgN = new CharConfig { sex = e.name.Contains("Хозяйка") ? 'f' : 'm', build = e.name.Contains("Хозяин") ? 3 : e.name.Contains("Пьяница") ? 0 : 1, height = e.name.Contains("Хозяйка") ? 166 : 176, armor = "cloth", weapon = "none" };
                        if (e.name.Contains("Хозяин") && L.inn != null) w = L.inn.bartender + L.origin;          // behind his counter
                        var ch = Character.Build(cfgN, actorMats, e.name); ch.go.transform.SetParent(L.root.transform, true); ch.go.transform.position = w;
                        GlyphLayer.Build(ch.go);
                        var fo = new Folk { id = e.id, name = e.name, ch = ch, act = new MotionActor(ch), L = L, p = w, yaw = (float)R.NextDouble() * 6.28f, drunk = e.name.Contains("Пьяница") };
                        fo.scene = e.name.Contains("Купец") ? "merchant" : e.name.Contains("Хозяйка") ? "hostess" : e.name.Contains("Пьяница") ? "drunkard" : null;
                        folk.Add(fo); L.colliders.Add(Collider2.Circle(w.x - L.origin.x, w.z - L.origin.z, 0.3f));
                    }
                    else if (e.type == "item" && e.name.StartsWith("Труп"))
                    {   // the knights' pieces by the waking place lie together: one knight in a full set, on his bones
                        if (e.name.Contains(":") && !e.name.Contains("шлем")) continue;
                        L.piles.Add(DeadKnight(L, w, (float)R.NextDouble() * 360, new List<int> { 0, 1, 2, 3, 4 }));
                    }
                    else if (e.type == "item") chests.Add(MakeChest(e, L, w, (float)R.NextDouble() * 360));
                }
                catch (System.Exception ex) { Debug.LogWarning("Не удалось поставить «" + e.name + "»: " + ex.Message); }
            }
            // the arena: two who came before, still in their armour (it can be taken off them)
            foreach (var c in smap.circles)
            {
                var L = SLocAt(c.c);
                for (int k = 0; k < 2; k++)
                {
                    float a = 0.9f + k * 2.6f + (float)R.NextDouble() * 0.6f; var m = c.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * c.r * 0.55f;
                    if (!L.Walk(m)) continue;
                    var w = L.ToWorld(m) - L.origin; float yaw = (float)R.NextDouble() * 360;
                    L.piles.Add(DeadKnight(L, L.ToWorld(m), yaw, new List<int> { 0, 1, 2, 3, 4 }));
                }
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
                armor = zombie ? "plate" : "cloth",
                weapon = kind == "drunk" ? "none" : boss ? "axe" : "sword",
                blade = boss ? "sabre" : rat ? "dagger" : null          // the rats: daggers; the chief: two sabres
            };
            var ch = Character.Build(cfgF, actorMats, name);
            var hb = ch.human;
            Transform[] tB = null; Vector3[] tRest = null; Transform tRoot = null; Vector3 tOff = Vector3.zero; float tSeg = 0;
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
                    // on the head: a ragged hood (the rats), the chief's spiked iron helm (KayKit, CC0)
                    if (boss) KKObj("bosshelm", rh.transform, new Vector3(0, 0.0f, 0.08f), 0.27f, Quaternion.identity);
                    else if (kind != "drunk" || id.GetHashCode() % 2 == 0) KKObj("rathood", rh.transform, new Vector3(0, -0.08f, 0.06f), 0.22f, Quaternion.identity);
                }
                if (boss)
                {   // a fur cape from the shoulders (KayKit, CC0)
                    var sp1 = hb.Bone("spine01") ?? hb.Bone("spine03"); var nk = hb.Bone("neck01");
                    if (sp1 != null && nk != null)
                    {
                        var cape = KKObj("furcape", ch.go.transform, Vector3.zero, 0.55f, Quaternion.identity);
                        cape.transform.position = nk.position - Vector3.up * (0.55f * 1.18f - 0.02f) - ch.go.transform.forward * 0.16f;
                        cape.transform.SetParent(sp1, true);
                    }
                }
                // the tail: a long bare tube from the base of the spine, curling down and back - skinned on a chain of
                // bones that a spring pulls after the body (it lags on a turn and lashes the other way, trails straight
                // out behind at a run)
                Transform pel = hb.Bone("pelvis") ?? hb.Bone("root") ?? hb.Bone("spine05");
                if (pel != null)
                {
                    var b0 = pel.position + ch.go.transform.forward * -0.12f; var fw = ch.go.transform.forward; var path = new List<Vector3>();
                    for (int k2 = 0; k2 <= TAIL_N; k2++) { float u = k2 / (float)TAIL_N; path.Add(b0 - fw * (0.75f * u * (boss ? 1.3f : 1)) + Vector3.down * (0.75f * u * u) + ch.go.transform.right * (0.18f * Mathf.Sin(u * 3.3f))); }
                    var tg = new GameObject("Tail"); tg.transform.SetParent(ch.go.transform, false);
                    var lp = new List<Vector3>(); foreach (var q in path) lp.Add(tg.transform.InverseTransformPoint(q));
                    float r0 = boss ? 0.04f : 0.03f; var tm = MeshGen.Tube(lp, r0, 6);
                    var tv = tm.vertices; int ring = 7; var bw = new BoneWeight[tv.Length];
                    for (int vi = 0; vi < tv.Length; vi++)
                    {
                        int ri = Mathf.Min(vi / ring, TAIL_N); float u = ri / (float)TAIL_N;
                        tv[vi] = lp[ri] + (tv[vi] - lp[ri]) * (1 - 0.7f * u);             // tapering to the tip
                        bw[vi] = new BoneWeight { boneIndex0 = ri, weight0 = 1 };
                    }
                    tm.vertices = tv; tm.boneWeights = bw; tm.RecalculateNormals(); tm.RecalculateBounds();
                    tB = new Transform[TAIL_N + 1]; var bp = new Matrix4x4[TAIL_N + 1]; tRest = new Vector3[TAIL_N + 1];
                    var cr = Quaternion.Inverse(ch.go.transform.rotation);
                    for (int k2 = 0; k2 <= TAIL_N; k2++)
                    {
                        var bt = new GameObject("TailBone" + k2).transform; bt.SetParent(ch.go.transform, false); bt.position = path[k2]; bt.rotation = ch.go.transform.rotation;
                        tB[k2] = bt; bp[k2] = bt.worldToLocalMatrix * tg.transform.localToWorldMatrix; tRest[k2] = cr * (path[k2] - path[0]);
                    }
                    tm.bindposes = bp;
                    var smr = tg.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = tm; smr.bones = tB; smr.rootBone = tB[0]; smr.updateWhenOffscreen = true;
                    smr.sharedMaterial = actorMats.Tint("SKIN", 0xc89a98u);
                    tRoot = pel; tOff = pel.InverseTransformPoint(b0); tSeg = (path[1] - path[0]).magnitude;
                    for (int k2 = 1; k2 < TAIL_N; k2++) tSeg = Mathf.Max(tSeg, (path[k2 + 1] - path[k2]).magnitude);
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
                }
            }
            BoneArm arm = null;
            if (zombie)
            {
                Transform head = hb.Bone("head"), neck = hb.Bone("neck01") ?? hb.Bone("neck");
                if (head != null && neck != null)
                {
                    var hp0 = head.position; head.localScale = Vector3.one * 0.02f;          // (the helmet goes with the head)
                    var sk = new GameObject("Skull").transform; sk.SetParent(ch.go.transform, false); sk.position = hp0 + Vector3.up * 0.1f; sk.rotation = ch.go.transform.rotation;
                    var hinge = new Vector3(0, -0.127f, 0.147f) * 0.2f;
                    KKObj("yskull", sk, Vector3.zero, 0.2f, Quaternion.identity);
                    var jaw = new GameObject("Jaw").transform; jaw.SetParent(sk, false); jaw.localPosition = hinge; jaw.localRotation = Quaternion.Euler(14, 0, 0);       // hanging a little open
                    KKObj("yjaw", jaw, -hinge, 0.2f, Quaternion.identity);
                    sk.SetParent(neck, true);
                }
                arm = BoneArm.Make(hb, "L", ch.go.transform, actorMats["BONE"]);
            }
            ch.go.transform.SetParent(L.root.transform, true); ch.go.transform.position = w; ch.go.transform.rotation = Quaternion.Euler(0, heading * Mathf.Rad2Deg, 0);
            GlyphLayer.Build(ch.go);
            var f = new Foe { id = id, name = name, kind = kind, ch = ch, act = new MotionActor(ch), L = L, p = w, home = w, goal = w };
            f.bones = arm; f.act.heading = heading;
            if (tB != null) { f.tailB = tB; f.tailRest = tRest; f.tailRoot = tRoot; f.tailOff = tOff; f.tailSeg = tSeg; } f.act.maxSpeed = boss ? player.maxSpeed * 1.05f : zombie ? 1.3f : player.maxSpeed;       // (as fast as the hero runs)
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
                var f = foes[i]; if (f.L != L || f.gone) continue;
                if (f.grabber) { UpdateGrabber(f, dt); continue; }
                // far from the hero they sleep (switched off: no thinking, no skinning)
                bool near = (new Vector2(f.p.x - pos.x, f.p.z - pos.z)).magnitude < 30f || f.noticed && !f.Dead;
                if (f.ch.go.activeSelf != near) f.ch.go.SetActive(near);
                if (!near) continue;
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
                    if (dist < 4f) BestMark("drunk", false);
                }
                else
                {
                    // seeing: in front, near enough, nothing between (walls hide him); close by he is heard
                    bool sees = alive && dist < SEE_R && (Vector2.Dot(fwd, toH / Mathf.Max(dist, 0.01f)) > SEE_COS || dist < 2.2f) && L.SightW(f.p + Vector3.up, pos + Vector3.up);
                    if (f.Boss && alive && dist < 7f) sees = true;          // (keen smell: the chief smells the hero through the rock)
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
                if (f.dDur > 0) { dir2 = null; mag = 0; lockP = new Vector2(pos.x, pos.z); }
                var dd = f.act.Locomotion(dt, dir2, mag, lockP, me, q => FoeCollide(f, q));
                dd += f.act.TakeAttackDrift() + extra;
                var np = FoeCollide(f, me + dd);
                if (f.dDur > 0)
                {   // a long dash along a set line: quick off, easing into its end
                    f.dT += dt; float u = Mathf.Clamp01(f.dT / f.dDur), e = 1 - (1 - u) * (1 - u);
                    var tgt = Vector3.Lerp(f.dFrom, f.dTo, e); np = FoeCollide(f, new Vector2(tgt.x, tgt.z));
                    f.trailT -= dt; if (f.trailT <= 0) { f.trailT = 0.05f; SpawnPuff(new Vector3(np.x, 0.6f, np.y), 0.05f, Vector3.zero, 0.35f); }
                    if (u >= 1) f.dDur = 0;
                }
                f.p = new Vector3(np.x, 0, np.y);
                f.ch.go.transform.position = f.p; f.ch.go.transform.rotation = Quaternion.Euler(0, f.act.heading * Mathf.Rad2Deg, 0);
                if (f.kind != "zombie") HunchReset(f);
                f.act.Animate(tNow, dt);
                if (f.kind != "zombie") RatBody(f, dist, dt);
                if (f.bones != null) f.bones.Follow();
                if (f.kind != "drunk") { StrikeHero(f, dist, toH); Bite(f, dist, toH, dt); }
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
            BestMark(f.kind, false);
            f.noticed = true; f.cd = Mathf.Max(f.cd, 0.25f);
            if (f.Boss) { mind = new BossMind(); if (!sDone.Contains("boss_intro")) { AutoSave(); Play("boss_intro"); } }
            else if (f.barkCd <= 0) { Bark(f.ch.go.transform, f.kind == "zombie" ? "zombie_moan" : Random.value < 0.5f ? "rat_notice" : "rat_notice2"); f.barkCd = 8; }
            Sound("draw", 0.35f, f.p);
        }
        /// not after the hero: walk somewhere near home, stop, look about, rummage, go on
        void Wander(Foe f, float dt, ref Vector2? dir2, ref float mag)
        {
            var me = new Vector2(f.p.x, f.p.z); var g = new Vector2(f.goal.x, f.goal.z);
            if ((g - me).magnitude > 0.5f && f.state != 0)
            {
                var sw = PathStep(f, f.goal, dt); var d = new Vector2(sw.x - f.p.x, sw.z - f.p.z);
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
            float reach = (f.act.Attacking ? f.act.Reach : 1.6f) * (f.Boss ? 1.15f : f.kind == "zombie" ? 1f : 0.85f);
            bool heroSwinging = player.Attacking && player.combatT > 0;
            // the walk to him (round the walls, if he is out of sight)
            Vector2 toward = toH;
            if (f.seenT > 0.2f) { var sw = PathStep(f, f.lastSeen, dt); toward = new Vector2(sw.x - f.p.x, sw.z - f.p.z); }
            if (!f.Boss)
            {
                // the simple ones: walk (run when far), strike when near; now and then a leap back from his blow
                bool zomb = f.kind == "zombie";
                if (zomb)
                {   // the risen: slow and stubborn, straight at him, a heavy blow now and then
                    if (dist > reach * 0.9f) { dir2 = toward.normalized; mag = dist > 4 ? 1.3f : 1f; }
                    if (dist < reach * 1.05f && f.cd <= 0 && !f.act.Attacking) { f.act.StartAttack(); f.swingHit = false; f.cd = 1.6f + Random.value * 0.8f; }
                    return;
                }
                // ---- a rat: hit and run. In at a run (or a leap with a bite), one stab, straight back out, a breath at a
                // distance circling him, and in again ----
                f.biteCd -= dt;
                if (f.act.Attacking && f.act.CutDone && f.retreatT <= 0) { f.retreatT = 0.9f + Random.value * 0.8f; Leap(f, -toH.normalized + new Vector2(-toH.y, toH.x).normalized * f.strafe * 0.4f, 6.5f); }
                if (f.retreatT > 0)
                {   // out of his reach, sideways round him, until the next run in
                    f.retreatT -= dt;
                    var side = new Vector2(-toH.y, toH.x).normalized * f.strafe;
                    dir2 = (side + toH.normalized * (dist < 3f ? -1f : dist > 4f ? 0.6f : 0f)).normalized; mag = 2f;
                    if (Random.value < dt * 0.5f) f.strafe = -f.strafe;
                    if (heroSwinging && dist < 2.4f && f.dodgeCd <= 0) { f.dodgeCd = 1f; Leap(f, -toH.normalized, 6f); }
                    return;
                }
                if (dist > reach * 0.9f) { dir2 = toward.normalized; mag = 2f; }                      // the run in
                if (f.biteCd <= 0 && f.biteT < 0 && dist > 1.4f && dist < 2.6f && !f.act.Attacking && Random.value < dt * 2f)
                { f.biteCd = 3f + Random.value * 2; f.biteT = 0.3f; Leap(f, toH, 7f); f.retreatT = 0; }  // a leap in with a bite (out again after)
                if (dist < reach * 1.05f && f.cd <= 0 && !f.act.Attacking) { f.act.StartAttack(); f.swingHit = false; f.cd = 0.4f; }
                if (heroSwinging && dist < 2.2f && f.dodgeCd <= 0 && !f.act.Attacking) { f.dodgeCd = 1.2f; if (Random.value < 0.3f) Leap(f, -toH.normalized, 6f); }
                return;
            }
            // ---- the chief: he learns the hero. He keeps count of how often the hero swings (and how evenly), which way he
            // dashes, how much he guards, how far he keeps, how often his own defence fails - and answers with it: a hero
            // who swings a lot meets parries and a riposte, one who dashes to a side finds him there, one who keeps away
            // gets a knife thrown at where he is going. His blows are hard to read: a short, quiet wind-up. ----
            var M = mind;
            if (!f.raged && f.hp < f.maxHp * 0.5f) { f.raged = true; Play("boss_rage"); f.parryCd = 0; }
            float tempo = f.raged ? 0.7f : 1f;
            M.Observe(this, f, dist, dt);
            f.triCd -= dt; f.summonCd -= dt;
            if (f.dDur > 0) return;                                   // (in the middle of a dash)
            if (f.seq > 0) { ChiefSeq(f, toH, dist, dt); return; }
            if (f.raged)
            {
                bool heroStill = player.Velocity.magnitude < 0.6f;
                // the hero stands and only swings: the blow meets nothing - a long dash back - and he comes straight back
                // in with a sabre; every blow of the hero's in a row makes it likelier (up to 90 %)
                if (heroSwinging && heroStill && dist < player.Reach + 0.6f && M.chain >= 1 && f.dodgeCd <= 0 && Random.value < M.EvadeK)
                {
                    f.dodgeCd = 0.5f; Sound("clash", 0.5f, f.p);
                    DashTo(f, f.p - new Vector3(toH.x, 0, toH.y).normalized * 3.2f, 0.28f); f.seq = 1; f.seqT = 0.18f; return;
                }
                // the hero hides behind his blade: two dashes along the legs of a right triangle to his back, a cut
                if (M.GuardSpam && f.triCd <= 0 && dist < 4.5f)
                {
                    f.triCd = 4.5f; var H = pos; var u = new Vector3(-toH.x, 0, -toH.y).normalized;          // from him to the chief
                    var A = f.p; var B = H - u * 1.3f; var mid = (A + B) / 2; var perp = Vector3.Cross(Vector3.up, (B - A).normalized) * (f.strafe >= 0 ? 1 : -1);
                    f.triB = B; DashTo(f, mid + perp * (B - A).magnitude / 2, 0.22f); f.seq = 10; return;
                }
                // the hero runs from him: a rat drops on the hero from above and holds him till the chief comes
                if (M.fleeT > 3.5f && f.summonCd <= 0 && dist > 5f) { f.summonCd = 18f; M.fleeT = 0; SummonGrabber(f); }
            }
            // a knife from his belt: when the hero keeps away (or has kept away of late), at where he will be
            f.throwCd -= dt;
            if (f.throwCd <= 0 && dist > 4.2f && dist < 11f && !f.act.Attacking && f.seenT <= 0.1f && (M.farK > 0.4f || dist > 6f))
            { ThrowKnife(f, M); f.throwCd = (M.farK > 0.6f ? 4f : 6.5f) + Random.value * 2f; f.windT = -1; }
            // defence: his blow coming, he is facing it
            if (heroSwinging && dist < player.Reach + 0.5f && f.dodgeCd <= 0 && !f.act.Attacking)
            {
                f.dodgeCd = f.raged ? 1.4f : 0.8f;
                float r = Random.value, pParry = (f.raged ? 0.6f : 1f) * M.ParryK, pDodge = M.DodgeK;
                if (f.parryCd <= 0 && r < pParry) { f.guardT = 0.45f; f.parryCd = f.raged ? 1.6f : 1.1f; f.parryRiposte = true; }          // the sabres up: turned aside, answered at once
                else if (r < pParry + 0.18f) { f.guardT = 0.5f; f.parryRiposte = false; }                                                    // a plain block: taken on crossed blades
                else if (r < pParry + 0.18f + pDodge)
                {   // a dash aside (to the side the hero does not favour) or straight back
                    float sideS = M.dashSide > 0.25f ? -1 : M.dashSide < -0.25f ? 1 : (Random.value < 0.5f ? 1 : -1);
                    var away = Random.value < 0.6f ? new Vector2(-toH.y, toH.x).normalized * sideS * 0.85f - toH.normalized * 0.35f : -toH.normalized;
                    away.Normalize(); DashTo(f, f.p + new Vector3(away.x, 0, away.y) * 2.6f, 0.26f);
                }
            }
            f.act.guardHeld = f.guardT > 0;
            if (f.guardT > 0) { mag = 0; return; }
            // a combination going on: the next blow
            if (f.combo > 0 && !f.act.Attacking && f.cd <= 0.25f)
            {
                if (dist < reach * 1.3f) { f.act.StartAttack(); f.swingHit = false; f.combo--; f.cd = 0.3f * tempo; }
                else { f.combo = 0; dir2 = toH.normalized; mag = 2f; }
            }
            // the quiet wind-up before a combination: a still moment, a little lower
            if (f.windT > 0) { f.windT -= dt; mag = 0; if (f.windT <= 0) { f.act.StartAttack(); f.swingHit = false; f.combo = M.ComboLen(); f.cd = (0.6f + Random.value * 0.6f) * tempo; } return; }
            if (dist > 3.2f) { dir2 = toward.normalized; mag = 2f; return; }
            if (f.act.Attacking) { if (dist > reach * 0.8f) { dir2 = toH.normalized; mag = 0.6f; } return; }
            if (player.guardHeld && f.cd > -0.6f && M.guardK > 0.35f)
            {   // he knows the hero waits behind his blade: round him, waiting for it to drop (or a knife, if far)
                var side = new Vector2(-toH.y, toH.x).normalized * (M.dashSide != 0 ? Mathf.Sign(M.dashSide) : f.strafe);
                dir2 = (side + toH.normalized * (dist > 2.6f ? 0.5f : dist < 1.8f ? -0.4f : 0)).normalized; mag = 1.1f;
                return;
            }
            if (f.cd <= 0)
            {
                if (dist < reach * 1.15f)
                {
                    if (Random.value < 0.2f && f.biteT < 0) { f.biteT = 0.25f; Leap(f, toH, 6.5f); f.combo = 1; f.cd = 0.4f; return; }      // the wererat's bite opens it
                    f.windT = f.raged ? 0.08f : 0.14f;
                }
                else { dir2 = toH.normalized; mag = 2f; }      // closing in at a run (where the hero is about to be)
            }
            else
            {   // between blows: at the edge of reach, on the side the hero likes to dash to
                float sideS = Mathf.Abs(M.dashSide) > 0.25f ? Mathf.Sign(M.dashSide) : f.strafe;
                var side = new Vector2(-toH.y, toH.x).normalized * sideS;
                float want = reach * 1.25f; dir2 = (side * 0.8f + toH.normalized * Mathf.Clamp(dist - want, -1, 1)).normalized; mag = 1f;
                if (Random.value < dt * 0.3f) f.strafe = -f.strafe;
            }
        }
        /// the chief's moves made of several steps
        void ChiefSeq(Foe f, Vector2 toH, float dist, float dt)
        {
            f.seqT -= dt; if (f.seqT > 0) return;
            switch (f.seq)
            {
                case 1:     // back from the dodge: straight at him, a cut
                    DashTo(f, pos - new Vector3(-toH.x, 0, -toH.y).normalized * 1.0f, 0.22f); f.seq = 2; f.seqT = 0.2f; break;
                case 2: f.act.StartAttack(); f.swingHit = false; f.cd = 0.5f; f.seq = 0; break;
                case 10:    // the second leg of the triangle: to his back
                    DashTo(f, f.triB, 0.22f); f.seq = 11; f.seqT = 0.24f; break;
                case 11: f.act.StartAttack(); f.swingHit = false; f.cd = 0.6f; f.combo = 1; f.seq = 0; break;
                default: f.seq = 0; break;
            }
        }
        /// a rat out of nowhere: drops on the hero from above, holds him fast; when the chief is on him it runs off
        void SummonGrabber(Foe chief)
        {
            var L = chief.L; var at = pos - new Vector3(Mathf.Sin(player.heading), 0, Mathf.Cos(player.heading)) * 0.45f;
            var g = MakeFoe("grab" + Time.frameCount, "Крысолюд", "rat", L, at, player.heading);
            g.grabber = true; g.noticed = true; g.hp = 999; g.grabbed = chief; g.p = at; g.dropY = 3.2f;
            foes.Add(g); grabT = 6f; Sound("draw", 0.6f, at);
            Bark(g.ch.go.transform, "rat_notice");
        }
        float grabT;
        bool HeroGrabbed => story && grabT > 0;
        /// the holding rat: falls onto the hero, clings behind him, lets go when the chief is near (or after six seconds),
        /// runs off out of sight and is gone
        void UpdateGrabber(Foe g, float dt)
        {
            var back = new Vector3(Mathf.Sin(player.heading), 0, Mathf.Cos(player.heading)) * 0.45f;
            if (g.dropY > 0)
            {   // falling
                g.dropY = Mathf.Max(0, g.dropY - dt * 9f);
                g.p = pos - back;
                g.ch.go.transform.position = g.p + Vector3.up * g.dropY; g.act.heading = player.heading; g.ch.go.transform.rotation = Quaternion.Euler(0, g.act.heading * Mathf.Rad2Deg, 0);
                g.act.Locomotion(dt, null, 0, null, new Vector2(g.p.x, g.p.z), null); g.act.Animate(tNow, dt);
                if (g.dropY <= 0) { Sound("hit_armor", 0.6f, pos); player.PokeIdle(); }
                return;
            }
            var chief = g.grabbed; bool hold = grabT > 0 && chief != null && !chief.Dead && (new Vector2(chief.p.x - pos.x, chief.p.z - pos.z)).magnitude > 1.6f;
            if (hold)
            {   // clinging behind him
                grabT -= dt;
                g.p = pos - back; g.act.heading = player.heading;
                g.ch.go.transform.position = g.p; g.ch.go.transform.rotation = Quaternion.Euler(0, g.act.heading * Mathf.Rad2Deg, 0);
                g.act.Locomotion(dt, null, 0, null, new Vector2(g.p.x, g.p.z), null); g.act.Animate(tNow, dt); RatTail(g, dt);
                return;
            }
            grabT = 0;
            // off it runs, away from the camera's side, and is gone once out of sight
            if (g.runDir == Vector2.zero) { var cf = sceneCam.transform.forward; g.runDir = new Vector2(cf.x, cf.z).normalized; if (g.runDir == Vector2.zero) g.runDir = Vector2.up; }
            var dd = g.act.Locomotion(dt, g.runDir, 2f, null, new Vector2(g.p.x, g.p.z), null);
            g.p += new Vector3(dd.x, 0, dd.y); g.ch.go.transform.position = g.p; g.ch.go.transform.rotation = Quaternion.Euler(0, g.act.heading * Mathf.Rad2Deg, 0);
            HunchReset(g); g.act.Animate(tNow, dt); RatBody(g, 99, dt);
            var vp = sceneCam.WorldToViewportPoint(g.p);
            if (vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f || (g.p - pos).magnitude > 18f) { g.gone = true; g.hp = 0; Destroy(g.ch.go); }
        }
        // ---- what the chief has learnt about the hero (one fight's memory) ----
        class BossMind
        {
            readonly List<float> swings = new List<float>(); public float dashSide, guardK, farK, hitsK, fleeT; float lastDashT = 9, lastAtk = -9;
            public int chain; float guardHold; readonly List<float> guardUps = new List<float>(); bool guardWas;
            public void Swing(float t) { swings.Add(t); while (swings.Count > 0 && t - swings[0] > 8f) swings.RemoveAt(0); chain = t - lastAtk < 1.6f ? chain + 1 : 1; lastAtk = t; }
            /// each blow of the hero's in a row: likelier to be met with a long dash back (up to 90 %)
            public float EvadeK => Mathf.Min(0.9f, 0.3f + 0.15f * chain);
            /// he holds his guard up, or keeps putting it up
            public bool GuardSpam => guardHold > 0.7f || guardUps.Count >= 3;
            public void Hit() { hitsK = Mathf.Min(1, hitsK + 0.25f); }
            public void Observe(Game g, Foe f, float dist, float dt)
            {
                guardK += ((g.player.guardHeld && dist < 4 ? 1 : 0) - guardK) * dt * 0.25f;
                bool gh = g.player.guardHeld; guardHold = gh ? guardHold + dt : 0;
                if (gh && !guardWas) guardUps.Add(Time.time); guardWas = gh; while (guardUps.Count > 0 && Time.time - guardUps[0] > 3f) guardUps.RemoveAt(0);
                if (Time.time - lastAtk > 1.6f) chain = 0;
                // running from him: far and getting further
                var away = new Vector2(g.pos.x - f.p.x, g.pos.z - f.p.z); var hv = new Vector2(g.player.Velocity.x, g.player.Velocity.z);
                bool flee = dist > 5f && Vector2.Dot(hv, away.normalized) > 1.2f;
                fleeT = flee ? fleeT + dt : Mathf.Max(0, fleeT - dt * 0.5f);
                farK += ((dist > 4.2f ? 1 : 0) - farK) * dt * 0.15f;
                hitsK = Mathf.Max(0, hitsK - dt * 0.05f);
                // a new dash of the hero's: to which side of the chief?
                if (g.dashT < lastDashT && g.dashT < 0.05f)
                {
                    var to = new Vector2(f.p.x - g.pos.x, f.p.z - g.pos.z).normalized; var dd = new Vector2(g.dashDir.x, g.dashDir.z);
                    float sd = Mathf.Sign(to.x * dd.y - to.y * dd.x);
                    dashSide = Mathf.Clamp(dashSide * 0.7f + sd * 0.3f, -1, 1);
                }
                lastDashT = g.dashT;
            }
            /// a hero who swings a lot (and evenly) is parried more
            public float ParryK
            {
                get
                {
                    float rate = swings.Count / 6f, even = 0;
                    if (swings.Count >= 4) { float m = 0, v = 0; int n = swings.Count - 1; for (int i = 1; i < swings.Count; i++) m += swings[i] - swings[i - 1]; m /= n; for (int i = 1; i < swings.Count; i++) v += Mathf.Pow(swings[i] - swings[i - 1] - m, 2); even = Mathf.Clamp01(1 - Mathf.Sqrt(v / n) / Mathf.Max(0.1f, m)); }
                    return Mathf.Clamp(0.12f + 0.45f * Mathf.Clamp01(rate / 1.2f) + 0.2f * even, 0.12f, 0.7f);
                }
            }
            /// the more he has been hit, the more he gets out of the way
            public float DodgeK => 0.25f + 0.35f * hitsK;
            public int ComboLen() { return Random.value < 0.5f ? 1 : Random.value < 0.6f ? 2 : 0; }
        }
        BossMind mind = new BossMind();
        // ---- thrown knives ----
        class Knife { public GameObject go; public Vector3 p, v; public float life; public StoryLocation L; public bool stuck; }
        readonly List<Knife> knives = new List<Knife>();
        void ThrowKnife(Foe f, BossMind M)
        {
            var from = f.p + Vector3.up * 1.3f; var hv = player.Velocity;
            float t = (new Vector2(pos.x - from.x, pos.z - from.z)).magnitude / 14f;
            var aim = pos + hv * t + Vector3.up * 1.0f;
            if (Mathf.Abs(M.dashSide) > 0.4f) { var to = (aim - from); var side = Vector3.Cross(Vector3.up, to.normalized); aim += side * Mathf.Sign(M.dashSide) * 0.6f; }      // (where he tends to dash)
            var go = new GameObject("Knife"); go.transform.SetParent(f.L.root.transform, true); go.transform.position = from;
            MeshGen.Obj(MeshGen.Box(0.025f, 0.3f, 0.012f), actorMats["STEEL"], go.transform, new Vector3(0, 0.08f, 0), Quaternion.identity, true, "Blade");
            MeshGen.Obj(MeshGen.Cylinder(0.012f, 0.012f, 0.1f, 6), actorMats["LEATHER2"], go.transform, new Vector3(0, -0.07f, 0), Quaternion.identity, true, "Grip");
            GlyphLayer.Build(go);
            knives.Add(new Knife { go = go, p = from, v = (aim - from).normalized * 14f, L = f.L });
            Sound("cloth", 0.6f, from); f.act.PokeIdle();
        }
        void UpdateKnives(float dt)
        {
            var L = loc as StoryLocation;
            for (int i = knives.Count - 1; i >= 0; i--)
            {
                var kn = knives[i]; kn.life += dt;
                if (kn.go == null || kn.life > 6f || kn.L != L) { if (kn.go != null) Destroy(kn.go); knives.RemoveAt(i); continue; }
                if (kn.stuck) continue;
                kn.v += Vector3.down * 3f * dt; kn.p += kn.v * dt;
                kn.go.transform.position = kn.p; kn.go.transform.rotation = Quaternion.LookRotation(kn.v) * Quaternion.Euler(90, 0, 0) * Quaternion.Euler(0, kn.life * 1400f, 0);
                var d = new Vector2(kn.p.x - pos.x, kn.p.z - pos.z);
                if (d.magnitude < 0.45f && hp > 0 && respawnT <= 0)
                {
                    var hf = new Vector2(Mathf.Sin(player.heading), Mathf.Cos(player.heading));
                    if (dashT < DASH_T + 0.15f) { }                                                     // (under the dash)
                    else if (player.guardHeld && Vector2.Dot(hf, d.normalized) > 0.2f) { Sound("clash", 0.7f, kn.p); kn.v = -kn.v * 0.3f; kn.life = 5.5f; continue; }
                    else { Sound("hit_flesh", 0.6f, kn.p); Damage(6); Destroy(kn.go); knives.RemoveAt(i); continue; }
                }
                if (kn.p.y < 0.05f || !L.Walk(L.ToMap(kn.p))) { kn.stuck = true; kn.life = Mathf.Max(kn.life, 3.5f); Sound("hit_stone", 0.4f, kn.p); }
            }
        }
        /// the next point on the way (the search is made at most twice a second per creature)
        Vector3 PathStep(Foe f, Vector3 goal, float dt)
        {
            f.stepT -= dt;
            if (f.stepT <= 0 || (f.L.ToWorld(f.step) - f.p).magnitude < 0.4f) { f.stepT = 0.5f + 0.2f * Random.value; f.step = f.L.NextStep(f.L.ToMap(f.p), f.L.ToMap(goal)); }
            return f.L.ToWorld(f.step);
        }
        /// rats stoop: the back bent forward, the head lifted to look ahead (laid over whatever the motion did)
        static readonly string[] HUNCH_B = { "spine04", "spine03", "spine01", "neck01", "head" };
        static readonly float[] HUNCH_D = { 10, 12, 10, -18, -8 };
        /// (the motion does not set every one of these bones each frame: the bend is laid over the pose they had before
        /// it, kept from the last frame - laid over itself frame after frame it turned the body round and round)
        static void HunchReset(Foe f)
        {
            var hb = f.ch.human;
            if (f.hunchRest == null) { f.hunchRest = new Quaternion[HUNCH_B.Length]; for (int i = 0; i < HUNCH_B.Length; i++) { var b = hb.Bone(HUNCH_B[i]); f.hunchRest[i] = b != null ? b.localRotation : Quaternion.identity; } }
            if (f.hunchSet == null) f.hunchSet = new Quaternion[HUNCH_B.Length];
            for (int i = 0; i < HUNCH_B.Length; i++)
            {
                var b = hb.Bone(HUNCH_B[i]); if (b == null) continue;
                // the motion left it as we bent it: put back what it was before the bend
                if (f.hunchOn) b.localRotation = f.hunchPre[i];
            }
        }
        static void Hunch(Foe f)
        {
            var hb = f.ch.human; var ax = f.ch.go.transform.right; float k2 = f.Boss ? 0.8f : 1f;
            if (f.hunchPre == null) f.hunchPre = new Quaternion[HUNCH_B.Length];
            for (int i = 0; i < HUNCH_B.Length; i++)
            {
                var b = hb.Bone(HUNCH_B[i]); if (b == null) continue;
                f.hunchPre[i] = b.localRotation;
                b.rotation = Quaternion.AngleAxis(HUNCH_D[i] * k2, ax) * b.rotation;
                f.hunchSet[i] = b.localRotation;
            }
            f.hunchOn = true;
        }
        /// running, a rat drops onto all fours: the back level, the head up, the arms reaching down to the ground and
        /// swinging like front legs (laid over the run of the legs); to fight he stands up on his hind legs again
        void Fours(Foe f, float dist, float dt)
        {
            float sp = f.act.Velocity.magnitude;
            bool fast = sp > f.act.maxSpeed * 0.6f || (f.noticed || f.grabber) && sp > 0.4f || f.fourK > 0.5f && sp > 0.25f;          // (a sneaking walk stays on the hind legs)
            bool on = fast && !f.act.Attacking && f.windT <= 0 && f.guardT <= 0 && f.biteT < 0 && f.kind != "drunk";       // (on all fours but to strike or guard)
            f.fourK = Mathf.MoveTowards(f.fourK, on ? 1 : 0, dt / (on ? 0.25f : 0.18f));
            if (f.fourK < 0.01f) return;
            float q = Mathf.SmoothStep(0, 1, f.fourK);
            f.fourPh += dt * sp / 1.5f;                     // a stride of 1.5 m
            var tr = f.ch.go.transform; var hb = f.ch.human; Vector3 ax = tr.right, fw = tr.forward;
            void B(string n, float deg) { var b = hb.Bone(n); if (b != null) b.rotation = Quaternion.AngleAxis(deg * q, ax) * b.rotation; }
            B("spine04", 16); B("spine03", 16); B("spine01", 12); B("neck01", -26); B("head", -16);
            foreach (var side in new[] { "L", "R" })
            {
                float sw = Mathf.Sin((f.fourPh + (side == "L" ? 0.5f : 0f)) * Mathf.PI * 2);
                var d = (-Vector3.up + fw * (0.25f + 0.45f * sw)).normalized;
                Transform u = hb.Bone("upperarm01." + side), lo = hb.Bone("lowerarm01." + side), w = hb.Bone("wrist." + side);
                if (u == null || lo == null || w == null) continue;
                var cu = (lo.position - u.position).normalized; u.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cu, d), q) * u.rotation;
                var cl = (w.position - lo.position).normalized; var dl = (d + fw * (sw < 0 ? 0.25f : -0.1f)).normalized;
                lo.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cl, dl), q) * lo.rotation;
            }
            tr.position = f.p + Vector3.down * (0.1f * q);
        }
        const int TAIL_N = 10;
        /// a rat's body language, laid over the motion: low on bent, heel-raised legs; the forearms held up before the
        /// chest like a rodent's front paws, the hands hanging; a nervous head that jerks round and holds, sniffs, looks
        /// over the shoulder, and washes the snout with both paws; curling up under the forearms to guard; jerking back
        /// when hit. Sharp starts and stops, no easing.
        void RatBody(Foe f, float dist, float dt)
        {
            var tr = f.ch.go.transform; var hb = f.ch.human;
            Hunch(f);
            // where the feet are before the body is lowered (they stay there: the knees bend instead)
            Transform[] hipB = new Transform[2], kneeB = new Transform[2], footB = new Transform[2];
            for (int s = 0; s < 2; s++)
            {
                string sd = s == 0 ? "L" : "R";
                hipB[s] = hb.Bone("upperleg01." + sd); kneeB[s] = hb.Bone("lowerleg01." + sd); footB[s] = hb.Bone("foot." + sd);
                if (footB[s] != null) f.feetA[s] = footB[s].position;
            }
            Fours(f, dist, dt);
            bool drunk = f.kind == "drunk";
            if (drunk) { RatTail(f, dt); return; }
            Vector3 ax = tr.right, fw = tr.forward, up = Vector3.up;
            float sp = f.act.Velocity.magnitude, four = Mathf.SmoothStep(0, 1, f.fourK), stand = 1 - four;
            bool atk = f.act.Attacking || f.windT > 0, curl = f.guardT > 0 && !f.Boss, cut = f.act.InCut;
            // hit: a jerk back, the tail lashing
            if (f.stagger > f.lastStag + 0.05f)
            {
                f.jerk = 1;
                if (f.tailQ != null) { float sg = Random.value < 0.5f ? -1 : 1; for (int i = 1; i <= TAIL_N; i++) f.tailQ[i] -= ax * (sg * 0.07f * i / TAIL_N); }
            }
            f.lastStag = f.stagger; f.jerk = Mathf.MoveTowards(f.jerk, 0, dt * 4);
            // -- the legs: crouched low, coiled lower to spring (the wind-up, a dodge), half up in the blow
            float cr = f.Boss ? 0.06f : 0.16f;
            if (curl) cr += 0.1f;
            if (f.leapT > 0) cr += 0.08f;
            if (f.windT > 0) cr *= 1.35f; else if (f.act.Attacking) cr *= 0.45f;
            f.crouch = Mathf.MoveTowards(f.crouch, cr * stand, dt * 1.6f);
            float heel = (f.Boss ? 0.03f : 0.06f) * stand;
            tr.position += Vector3.down * f.crouch;
            for (int s = 0; s < 2; s++)
            {
                if (hipB[s] == null || kneeB[s] == null || footB[s] == null) continue;
                var fq = footB[s].rotation;
                LegIK(hipB[s], kneeB[s], footB[s], f.feetA[s] + up * heel, fw + ax * (s == 0 ? -0.2f : 0.2f));
                footB[s].rotation = Quaternion.AngleAxis(32 * heel / 0.06f, ax) * fq;          // up on the toes
            }
            // -- the nervous head (not the chief: he does not fidget)
            if (!f.Boss)
            {
                f.fidT -= dt;
                if (f.fidT <= 0)
                {
                    if (f.fid != 0 || f.lookT != 0) { f.fid = 0; f.lookT = 0; f.fidT = cut ? 0.25f + 0.4f * Random.value : 0.4f + 1.2f * Random.value; }
                    else if (f.noticed) { f.lookT = (Random.value < 0.5f ? -1 : 1) * (8 + 10 * Random.value); f.fidT = 0.15f + 0.25f * Random.value; }
                    else
                    {
                        float r = Random.value; bool still = sp < 0.2f && !cut;
                        if (r < 0.45f || cut || sp > 1.5f) { f.fid = 1; f.lookT = (Random.value < 0.5f ? -1 : 1) * (sp > 1.5f ? 30 : 45 + 45 * Random.value); f.fidT = 0.35f + 0.8f * Random.value; }
                        else if (r < 0.68f) { f.fid = 2; f.fidT = 0.9f + 0.8f * Random.value; }
                        else if (r < 0.84f && still) { f.fid = 3; f.fidT = 1.6f + 1.2f * Random.value; }
                        else { f.fid = 4; f.lookT = (Random.value < 0.5f ? -1 : 1) * 120; f.fidT = 0.6f + 0.5f * Random.value; }
                    }
                }
                if (f.fid == 3 && (sp > 0.3f || atk || f.noticed)) { f.fid = 0; f.fidT = 0.3f; }
            }
            f.lookY = Mathf.MoveTowards(f.lookY, f.lookT, 900 * dt);          // a snap, then a hold
            // -- the arms: the paws before the chest; washing; curled up under the forearms
            bool free = !atk && !cut && !f.Boss && f.grabbed == null;
            f.pawK = Mathf.MoveTowards(f.pawK, free && !curl ? stand : 0, dt * 5);
            f.washK = Mathf.MoveTowards(f.washK, f.fid == 3 ? 1 : 0, dt * 4);
            f.curlK = Mathf.MoveTowards(f.curlK, curl ? 1 : 0, dt * 9);
            float pw = f.pawK * (1 - f.curlK);
            if (pw > 0.01f || f.curlK > 0.01f)
                foreach (var side in new[] { "L", "R" })
                {
                    Transform u = hb.Bone("upperarm01." + side), lo = hb.Bone("lowerarm01." + side), w = hb.Bone("wrist." + side);
                    if (u == null || lo == null || w == null) continue;
                    var sv = ax * (side == "L" ? -1 : 1);
                    float rub = Mathf.Sin(f.t * 16 + (side == "L" ? 0 : 3.1f));
                    Vector3 du = Vector3.Lerp((-up * 0.85f + fw * 0.45f + sv * 0.12f).normalized, (-up * 0.35f + fw * 0.9f + sv * 0.1f).normalized, f.washK);
                    Vector3 dl = Vector3.Lerp((fw * 0.75f + up * 0.35f - sv * 0.25f).normalized, (up * 0.95f + fw * (0.05f + 0.15f * rub) - sv * 0.3f).normalized, f.washK);
                    du = Vector3.Slerp(du, (fw * 0.7f + up * 0.55f + sv * 0.2f).normalized, f.curlK);          // forearms over the head
                    dl = Vector3.Slerp(dl, (-sv * 0.85f + up * 0.35f + fw * 0.1f).normalized, f.curlK);
                    float k = Mathf.Max(pw, f.curlK);
                    var cu = (lo.position - u.position).normalized; u.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cu, du), k) * u.rotation;
                    var cl = (w.position - lo.position).normalized; lo.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cl, dl), k) * lo.rotation;
                    w.rotation = Quaternion.AngleAxis(70 * pw * (1 - f.washK), ax) * w.rotation;          // the hands hang
                }
            // -- the spine and the head: the curl, the jerk back, the sniff, the wash, the look round
            void B(string n, float deg, Vector3 axis) { if (Mathf.Abs(deg) < 0.01f) return; var b = hb.Bone(n); if (b != null) b.rotation = Quaternion.AngleAxis(deg, axis) * b.rotation; }
            float sniff = f.fid == 2 ? 1 : 0;
            B("spine03", 12 * f.curlK - 16 * f.jerk, ax); B("spine01", 8 * f.curlK - 10 * f.jerk, ax);
            B("head", 22 * f.curlK + 18 * f.washK - 22 * sniff + sniff * 4 * Mathf.Sin(f.t * 45) + f.jerk * 10 * Mathf.Sin(f.t * 60), ax);
            B("neck01", -10 * sniff, ax);
            float ly = f.lookY;
            B("spine03", ly * 0.25f, up); B("neck01", ly * 0.35f, up); B("head", ly * 0.4f, up);
            RatTail(f, dt);
        }
        static void LegIK(Transform hip, Transform knee, Transform foot, Vector3 target, Vector3 pole)
        {
            Vector3 hp = hip.position, kp = knee.position, ap = foot.position;
            float l1 = (kp - hp).magnitude, l2 = (ap - kp).magnitude; if (l1 < 1e-4f || l2 < 1e-4f) return;
            Vector3 toT = target - hp; float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(l1 - l2) + 0.01f, (l1 + l2) * 0.995f); toT.Normalize();
            Vector3 perp = pole - toT * Vector3.Dot(pole, toT); if (perp.sqrMagnitude < 1e-8f) return; perp.Normalize();
            float a = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
            Vector3 nk = hp + toT * (Mathf.Cos(a) * l1) + perp * (Mathf.Sin(a) * l1);
            hip.rotation = Quaternion.FromToRotation((kp - hp).normalized, (nk - hp).normalized) * hip.rotation;
            Vector3 kp2 = knee.position, ap2 = foot.position;
            knee.rotation = Quaternion.FromToRotation((ap2 - kp2).normalized, (hp + toT * d - kp2).normalized) * knee.rotation;
        }
        /// the tail: each joint pulled toward its place (stiff at the root, loose at the tip, so the tip lags and
        /// lashes), drooping to the floor at rest, straight out behind at a run; kept above the ground
        void RatTail(Foe f, float dt)
        {
            if (f.tailB == null || f.tailRoot == null || dt <= 0) return;
            var tr = f.ch.go.transform; int N = TAIL_N;
            Vector3 root = f.tailRoot.TransformPoint(f.tailOff);
            Vector3 runDir = (-tr.forward * 0.985f + Vector3.up * 0.17f).normalized;
            float runK = Mathf.Clamp01(f.act.Velocity.magnitude / Mathf.Max(0.1f, f.act.maxSpeed * 0.7f)) * (f.Dead ? 0 : 1);
            float ground = f.p.y + 0.025f;
            bool fresh = f.tailP == null || (f.tailP[0] - root).sqrMagnitude > 1f;
            if (fresh) { f.tailP = new Vector3[N + 1]; f.tailQ = new Vector3[N + 1]; }
            float kRoot = 1 - Mathf.Exp(-dt * 24), kTip = 1 - Mathf.Exp(-dt * 4.5f);
            for (int i = 0; i <= N; i++)
            {
                float u = i / (float)N;
                Vector3 rest = tr.rotation * f.tailRest[i], run = runDir * (f.tailSeg * i) + tr.right * (0.04f * u * Mathf.Sin(f.t * 9 + i * 0.7f));
                Vector3 tgt = root + Vector3.Lerp(rest, run, runK) + tr.right * (0.03f * u * Mathf.Sin(Time.time * 1.7f + i * 0.5f));
                if (fresh) { f.tailP[i] = f.tailQ[i] = tgt; continue; }
                if (i == 0) { f.tailP[0] = f.tailQ[0] = root; continue; }
                Vector3 v = f.tailP[i] - f.tailQ[i]; f.tailQ[i] = f.tailP[i];
                f.tailP[i] += v * 0.9f + Vector3.down * (2f * dt * dt);
                f.tailP[i] += (tgt - f.tailP[i]) * Mathf.Lerp(kRoot, kTip, u);
            }
            for (int it = 0; it < 2; it++)
                for (int i = 1; i <= N; i++)
                {
                    var d = f.tailP[i] - f.tailP[i - 1]; float m = d.magnitude;
                    if (m > 1e-5f) f.tailP[i] = f.tailP[i - 1] + d * (f.tailSeg / m);
                    if (f.tailP[i].y < ground) f.tailP[i].y = ground;
                }
            for (int i = 0; i <= N; i++)
            {
                var b = f.tailB[i]; b.position = f.tailP[i];
                int a0 = Mathf.Min(i, N - 1);
                Vector3 restD = tr.rotation * (f.tailRest[a0 + 1] - f.tailRest[a0]), curD = f.tailP[a0 + 1] - f.tailP[a0];
                b.rotation = (curD.sqrMagnitude > 1e-8f && restD.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(restD, curD) : Quaternion.identity) * tr.rotation;
            }
        }
        /// a long dash to a point (the chief): in a set time, from where he stands
        void DashTo(Foe f, Vector3 to, float time)
        {
            var d = new Vector2(to.x - f.p.x, to.z - f.p.z); var c = FoeCollide(f, new Vector2(f.p.x, f.p.z) + d);
            f.dFrom = f.p; f.dTo = new Vector3(c.x, 0, c.y); f.dDur = time; f.dT = 0; f.leapT = 0;
            SpawnPuff(f.p + Vector3.up * 0.5f, 0.06f, Vector3.zero, 0.5f); Sound("cloth", 0.6f, f.p);
        }
        void Leap(Foe f, Vector2 d, float sp) { f.leapV = d.normalized * sp; f.leapT = 0.28f; SpawnPuff(f.p + Vector3.up * 0.3f, 0.04f, Vector3.zero, 0.4f); Sound("cloth", 0.5f, f.p); }

        void Bite(Foe f, float dist, Vector2 toH, float dt)
        {
            if (f.biteT < 0) return;
            f.biteT -= dt; if (f.biteT > 0) return; f.biteT = -1;
            if (dist > 1.2f || hp <= 0 || respawnT > 0 || dashT < DASH_T + 0.15f) return;
            var hf = new Vector2(Mathf.Sin(player.heading), Mathf.Cos(player.heading));
            if (player.guardHeld && Vector2.Dot(hf, -toH / Mathf.Max(dist, 0.01f)) > 0.2f) { Sound("clash", 0.5f, f.p); f.stagger = 0.4f; return; }
            Sound("hit_flesh", 0.6f, f.p); Damage(f.Boss ? 7 : 5);
        }
        /// his blow reaches the hero: a guard facing it turns it (the chief's second blow of a combination breaks through),
        /// the dash goes under it
        void StrikeHero(Foe f, float dist, Vector2 toH)
        {
            if (!f.act.Cutting || f.swingHit || hp <= 0 || respawnT > 0) return;
            float reach = f.act.Reach * (f.Boss ? 1.15f : f.kind == "zombie" ? 1f : 0.85f);
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
                var f = foes[i]; if (f.L != L || f.Dead || f.grabber || player.atkHits.Contains(1000 + i)) continue;
                var d = new Vector2(f.p.x - pos.x, f.p.z - pos.z); float dist = d.magnitude;
                if (dist > player.Reach + (f.Boss ? 0.15f : 0) || (Vector2.Dot(hf, d / Mathf.Max(dist, 0.01f)) < 0.3f && !player.Around)) continue;
                player.atkHits.Add(1000 + i);
                if (f.leapT > 0.1f) continue;                // he is out of the way
                if (f.kind == "drunk") { f.kind = "rat"; f.act.StopCut(); }          // you hit him, he fights
                var ff = new Vector2(Mathf.Sin(f.act.heading), Mathf.Cos(f.act.heading));
                if (f.act.guardHeld && Vector2.Dot(ff, -d / Mathf.Max(dist, 0.01f)) > 0.3f)
                {   // parried: steel on steel, and he answers at once
                    Sound("clash", 0.9f, f.p); SpawnPuff((f.p + pos) / 2 + Vector3.up * 1.2f, 0.03f, Vector3.up, 0.3f);
                    f.guardT = 0; f.act.guardHeld = false;
                    if (f.parryRiposte || !f.Boss) { f.cd = 0; f.combo = 1; f.act.StartAttack(); f.swingHit = false; }      // parried: answered at once
                    else f.cd = 0.3f;                                                                                       // only blocked
                    player.combatT = Mathf.Max(player.combatT, 1);
                    continue;
                }
                int dmg = cfg.weapon == "none" ? 6 : 10;
                f.hp -= dmg; f.stagger = f.Boss ? 0.12f : 0.35f; Sound("hit_flesh", 0.7f, f.p);
                if (f.Boss) mind.Hit();
                if (!f.noticed) Notice(f);
                f.lastSeen = pos; f.seenT = 0;
                if (!f.Boss && !f.hurtBark && f.kind == "rat" && Random.value < 0.6f) { f.hurtBark = true; Bark(f.ch.go.transform, "rat_hurt"); }
                if (f.Dead && f.kind == "zombie" && !f.fortitudeUsed && Random.value < 0.5f)
                {   // undead fortitude: it should be down, and it is not (once)
                    f.fortitudeUsed = true; f.hp = 1; f.stagger = 1.2f; Bark(f.ch.go.transform, "zombie_moan"); continue;
                }
                if (f.Dead) Die(f);
            }
        }
        void Die(Foe f)
        {
            BestMark(f.kind, true);
            f.ch.go.transform.position = f.p;          // (out of the crouch: he falls from where he stands)
            f.deadT = 0; f.act.guardHeld = false; f.act.PlayCut("die", false, false, 1, true); SpawnPuff(f.p + Vector3.up * 0.8f, 0.06f, Vector3.up * 0.4f, 0.8f);
            Sound("plates", 0.6f, f.p);
            if (f.Boss) { Play("boss_dead"); autoAfterScene = true; }
        }
        /// he falls (backwards, with a twist) and stays where he fell
        void DeadPose(Foe f, float dt)
        {
            if (f.deadT > 3.5f) return;                  // lying still: nothing more to move
            f.deadT += dt;
            f.act.Locomotion(dt, null, 0, null, new Vector2(f.p.x, f.p.z), null); f.act.Animate(tNow, dt);
            if (f.bones != null) f.bones.Follow();
            RatTail(f, dt);
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
                if (e.type != "trigger" || SLocAt(e.p) != L) continue;
                var tw = SWorld(L, e.p); if ((new Vector2(tw.x - pos.x, tw.z - pos.z)).magnitude > (L.inn != null ? 2.5f : 4.5f)) continue;
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
            AutoSave();                    // (at the start of a scene)
            sDir = new Running { s = s, tier = s.tier }; Next();
        }
        void Bark(Transform at, string id)
        {
            DScene s; if (!scenes.TryGetValue(id, out s)) return;
            foreach (var l in s.lines) if (l.text != null) { barks.Add(new BarkLine { at = at, who = l.who, text = l.text, dur = 1.6f + 0.05f * l.text.Length }); PlayVoice(l.who, l.text); break; }
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
                PlayVoice(l.who, l.text);
                return;
            }
        }
        void End()
        {
            string id = sDir.s.id; sDir = null; autoAfterScene = true;
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
        float StoryCapMag(float mag) { if (HeroGrabbed) return 0; return story && sDir != null && sDir.tier == 2 ? Mathf.Min(mag, 1f) : mag; }

        /// once a frame in the world, after the hero has moved
        void StoryUpdate(float dt)
        {
            if (!story || !(loc is StoryLocation)) return;
            StoryLight(false);
            visT -= dt;
            if (visT <= 0)
            {
                visT = 0.1f; var SL = (StoryLocation)loc;
                if (SL.useVis) { Vector4 vr; var tex = SL.VisUpdate(pos, out vr); Shader.SetGlobalTexture("_VisTex", tex); Shader.SetGlobalVector("_VisRect", vr); Shader.SetGlobalFloat("_VisOn", 1); }
                else Shader.SetGlobalFloat("_VisOn", 0);
            }
            autoT += dt; if (autoT > 300f) { autoT = 0; AutoSave(); }          // every five minutes
            if (autoAfterScene && sDir == null) { autoAfterScene = false; AutoSave(); }
            HeroHitsFoes(); UpdateFoes(dt); UpdateKnives(dt); UpdateFolk(dt); UpdateChests(dt); UpdateSkull(dt);
            StoryTriggers(); StoryPassages(dt); UpdateDirector(dt); StoryIdleTalk(dt);
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
        // the skull in the pocket: a little cloud of its own by the hero, the skull's own render on it
        void PocketSkull(Rect lp, float alpha)
        {
            if (!sHasSkull || alpha < 0.01f) return;
            var r = SkullRect(lp);
            AddPiece(r, r.width / 2, false, 0, alpha);
            if (skullAscii != null && skullAscii.final != null && keyMat != null) { keyMat.SetFloat("_KeyAlpha", alpha); Graphics.DrawTexture(r, skullAscii.final, keyMat); }

        }
        // ---- the pocket map of a story place: its ground by biome, its walls, its ways out, chests ----
        Location mapFor;
        void StoryMapCheck()
        {
            var L = story ? loc as StoryLocation : null;
            if (L == mapFor) return;
            if (mapTex != null) Destroy(mapTex); mapTex = null; mapFor = L;
            if (L == null) return;          // (the test world builds its own)
            mapWorld = L.WorldRect;
            int W = Mathf.Max(4, Mathf.CeilToInt(mapWorld.width * MAP_PPM)), H = Mathf.Max(4, Mathf.CeilToInt(mapWorld.height * MAP_PPM));
            var px = new Color32[W * H]; var walk = new bool[W * H]; var bio = new string[W * H];
            for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
                {
                    var w = new Vector3(mapWorld.x + (i + 0.5f) / MAP_PPM, 0, mapWorld.y + (j + 0.5f) / MAP_PPM); var m = L.ToMap(w);
                    walk[j * W + i] = L.Walk(m); bio[j * W + i] = L.BiomeAt(m);
                }
            for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
                {
                    int ix = j * W + i; Color32 c;
                    if (walk[ix])
                    {
                        string b = bio[ix]; float n = Mathf.PerlinNoise(i * 0.18f, j * 0.18f);
                        c = C32(b == "meadow" ? 0x1c3319u : b == "tavern" ? 0x5e5044u : 0x3a3836u); c.a = (byte)((b == "meadow" && n > 0.42f ? 2 : 1) * 16);
                    }
                    else
                    {
                        bool edge = false;
                        for (int dj = -2; dj <= 2 && !edge; dj++) for (int di = -2; di <= 2; di++) { int a = i + di, bb = j + dj; if (a >= 0 && bb >= 0 && a < W && bb < H && walk[bb * W + a]) { edge = true; break; } }
                        if (!edge) continue;
                        c = C32(0x6a6670u); c.a = 9 * 16;
                    }
                    px[ix] = c;
                }
            void Spot(Vector3 w, float r, uint col, int cls)
            {
                int ci = Mathf.FloorToInt((w.x - mapWorld.x) * MAP_PPM), cj = Mathf.FloorToInt((w.z - mapWorld.y) * MAP_PPM), R = Mathf.CeilToInt(r * MAP_PPM);
                for (int j = cj - R; j <= cj + R; j++) for (int i = ci - R; i <= ci + R; i++) { if (i < 0 || j < 0 || i >= W || j >= H || (i - ci) * (i - ci) + (j - cj) * (j - cj) > R * R) continue; var c = C32(col); c.a = (byte)(cls * 16); px[j * W + i] = c; }
            }
            foreach (var g in L.passages) Spot(L.ToWorld(g.P(L.region)), 1.1f, 0xe8b443u, 11);
            foreach (var c in chests) if (c.L == L) Spot(c.p, 0.5f, 0xb08860u, 8);
            mapTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "StoryMap" };
            mapTex.SetPixels32(px); mapTex.Apply(false, true);
        }
        // ---- a knight who died here: the plate armour of the game lying as it fell on his bones. Each piece of the
        // armour is the hero's own (baked from a knight in the pose of the fall); under it the skeleton - long bones
        // between the joints, ribs, a pelvis, the skull in the helmet. A piece taken (for the limb it covers) shows
        // the bones beneath. ----
        ArmourPile DeadKnight(StoryLocation L, Vector3 w, float yaw, IList<int> have)
        {
            var pile = new ArmourPile { pos = w - L.origin, root = new GameObject("DeadKnight") };
            var P = pile.root.transform; P.SetParent(L.root.transform, false); P.position = w; P.rotation = Quaternion.Euler(0, yaw, 0);
            var ch = Character.Build(new CharConfig { sex = 'm', build = 0, height = 176, armor = "plate", weapon = "none" }, actorMats, "Fallen");
            ch.go.transform.position = w; ch.go.transform.rotation = P.rotation;
            var act = new MotionActor(ch); act.PlayCut("die", false, false, 1, true);
            for (int i = 0; i < 100; i++) { act.Locomotion(1 / 30f, null, 0, null, new Vector2(w.x, w.z), null); act.Animate(i / 30f, 1 / 30f); }
            var lob = Character.LimbOfBone(); HumanDB.Load();
            int LimbOf(Transform b) { while (b != null) { int bi; if (HumanDB.BI.TryGetValue(b.name, out bi)) return lob[bi] - 1; b = b.parent; } return -1; }
            var groups = new Transform[5]; Transform torso = new GameObject("Torso").transform; torso.SetParent(P, true);
            for (int l = 0; l < 5; l++) { groups[l] = new GameObject("Limb" + l).transform; groups[l].SetParent(P, true); }
            var skip = new HashSet<Material> { actorMats["SKIN"], actorMats["HAIR"], actorMats["EYE"], actorMats["SHADOW"] };
            foreach (var r in ch.go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial == null || skip.Contains(r.sharedMaterial)) continue;
                Mesh m; Transform at = r.transform; int limb;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null)
                {
                    m = new Mesh(); smr.BakeMesh(m, true);
                    // the limb most of its vertices hang on
                    var cnt = new int[6]; var bw = smr.sharedMesh.boneWeights;
                    foreach (var b in bw) { var bt = b.boneIndex0 < smr.bones.Length ? smr.bones[b.boneIndex0] : null; cnt[LimbOf(bt) + 1]++; }
                    int best = 0; for (int q = 1; q < 6; q++) if (cnt[q] > cnt[best]) best = q; limb = best - 1;
                }
                else { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue; m = Instantiate(mf.sharedMesh); limb = LimbOf(r.transform); var v = m.vertices; var sc = r.transform.lossyScale; for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], sc); m.vertices = v; }
                m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false);
                var go = MeshGen.Obj(m, r.sharedMaterial, limb >= 0 ? groups[limb] : torso, Vector3.zero, Quaternion.identity, true, "Piece");
                go.transform.position = at.position; go.transform.rotation = at.rotation;
            }
            // the skeleton under it
            var hb = ch.human; var bone = mats["BONE"];
            void Stick(string a, string b, float r0, Transform par)
            {
                Transform ta = hb.Bone(a), tb = hb.Bone(b); if (ta == null || tb == null) return;
                var d = tb.position - ta.position; if (d.magnitude < 0.02f) return;
                var g = MeshGen.Obj(MeshGen.Cylinder(r0, r0 * 0.75f, d.magnitude, 6), bone, par, Vector3.zero, Quaternion.identity, false, "Bone");
                g.transform.position = ta.position; g.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            }
            var skel = new GameObject("Bones").transform; skel.SetParent(P, true);
            foreach (var sd in new[] { "L", "R" })
            {
                Stick("upperarm01." + sd, "lowerarm01." + sd, 0.022f, skel); Stick("lowerarm01." + sd, "wrist." + sd, 0.016f, skel);
                Stick("upperleg01." + sd, "lowerleg01." + sd, 0.03f, skel); Stick("lowerleg01." + sd, "foot." + sd, 0.022f, skel);
            }
            Stick("spine05", "neck01", 0.025f, skel);
            var sp = hb.Bone("spine03"); if (sp != null) { var rib = MeshGen.Obj(MeshGen.Sphere(0.14f, 10, 7), bone, skel, Vector3.zero, Quaternion.identity, false, "Ribs"); rib.transform.position = sp.position; rib.transform.localScale = new Vector3(1, 0.6f, 1.25f); }
            Transform nk = hb.Bone("neck01"), hd = hb.Bone("head");
            if (nk != null && hd != null) { var up = (hd.position - nk.position).normalized; var sk = KKObj("yskull", skel, Vector3.zero, 0.19f, Quaternion.identity); KKObj("yjaw", sk.transform, Vector3.zero, 0.19f, Quaternion.identity); sk.transform.position = hd.position + up * 0.08f; sk.transform.rotation = Quaternion.LookRotation(Vector3.up, up); }
            foreach (var sd in new[] { "L", "R" })
            {
                var wr = hb.Bone("wrist." + sd); if (wr == null) continue;
                for (int k = 0; k < 4; k++) { var f = MeshGen.Obj(MeshGen.Cylinder(0.006f, 0.004f, 0.08f, 5), bone, skel, Vector3.zero, Quaternion.identity, false, "Finger"); f.transform.position = wr.position + wr.right * ((k - 1.5f) * 0.014f); f.transform.rotation = wr.rotation; }
            }
            Destroy(ch.go);
            for (int l = 0; l < 5; l++) { if (have.Contains(l)) pile.items[l] = groups[l].gameObject; else groups[l].gameObject.SetActive(false); }
            GlyphLayer.Build(pile.root);
            return pile;
        }
        // ---- KayKit pieces (CC0, Kay Lousberg): Resources/kaykit.bytes, each a few one-colour meshes, 1 m wide ----
        static Dictionary<string, List<KeyValuePair<Mesh, uint>>> KK;
        GameObject KKObj(string name, Transform parent, Vector3 localPos, float scale, Quaternion rot)
        {
            if (KK == null)
            {
                KK = new Dictionary<string, List<KeyValuePair<Mesh, uint>>>();
                var b = Resources.Load<TextAsset>("kaykit").bytes; int n = System.BitConverter.ToInt32(b, 0), o = 4;
                for (int k = 0; k < n; k++)
                {
                    int ln = b[o]; string nm = System.Text.Encoding.ASCII.GetString(b, o + 1, ln); o += 1 + ln;
                    uint col = System.BitConverter.ToUInt32(b, o); int nv = System.BitConverter.ToInt32(b, o + 4), nt = System.BitConverter.ToInt32(b, o + 8); o += 12;
                    var v = new Vector3[nv]; for (int i = 0; i < nv; i++, o += 12) v[i] = new Vector3(System.BitConverter.ToSingle(b, o), System.BitConverter.ToSingle(b, o + 4), System.BitConverter.ToSingle(b, o + 8));
                    var t = new int[nt * 3]; for (int i = 0; i < t.Length; i++, o += 4) t[i] = System.BitConverter.ToInt32(b, o);
                    string key = nm.Split('#')[0]; List<KeyValuePair<Mesh, uint>> l; if (!KK.TryGetValue(key, out l)) { l = new List<KeyValuePair<Mesh, uint>>(); KK[key] = l; }
                    var m = new Mesh { name = nm }; m.vertices = v; m.uv = new Vector2[nv]; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
                    l.Add(new KeyValuePair<Mesh, uint>(m, col));
                }
            }
            var go = new GameObject("KK_" + name); go.transform.SetParent(parent, false); go.transform.localPosition = localPos; go.transform.localRotation = rot;
            foreach (var kv in KK[name])
            {
                // the size is baked into the vertices (the characters are laid out on the mesh as it is)
                var src = kv.Key; var vv = src.vertices; for (int i = 0; i < vv.Length; i++) vv[i] *= scale;
                var m = new Mesh { name = src.name }; m.vertices = vv; m.uv = new Vector2[vv.Length]; m.triangles = src.triangles; m.RecalculateNormals(); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false);
                var mat = actorMats.Tint("LEATHER", kv.Value);
                MeshGen.Obj(m, mat, go.transform, Vector3.zero, Quaternion.identity, true, "piece");
            }
            return go;
        }
        /// an arm down to the bone: the flesh and the armour of the limb shrink to nothing, two bones and a few
        /// knuckles follow its joints (worked out from the bones' turns and their lengths at rest)
        public class BoneArm
        {
            Transform up, lo; Vector3 dU, dL; float lU, lL; Transform mU, mL, mH;
            public static BoneArm Make(Human hb, string side, Transform parent, Material bone)
            {
                Transform u = hb.Bone("upperarm01." + side), l = hb.Bone("lowerarm01." + side), w = hb.Bone("wrist." + side);
                if (u == null || l == null || w == null) return null;
                var a = new BoneArm { up = u, lo = l };
                a.lU = (l.position - u.position).magnitude; a.lL = (w.position - l.position).magnitude;
                a.dU = Quaternion.Inverse(u.rotation) * (l.position - u.position).normalized; a.dL = Quaternion.Inverse(l.rotation) * (w.position - l.position).normalized;
                a.mU = MeshGen.Obj(MeshGen.Cylinder(0.022f, 0.016f, a.lU, 7), bone, parent, Vector3.zero, Quaternion.identity, true, "Humerus").transform;
                a.mL = MeshGen.Obj(MeshGen.Cylinder(0.016f, 0.012f, a.lL, 7), bone, parent, Vector3.zero, Quaternion.identity, true, "Ulna").transform;
                var hand = new GameObject("BoneHand").transform; hand.SetParent(parent, false); a.mH = hand;
                for (int k = 0; k < 4; k++) MeshGen.Obj(MeshGen.Cylinder(0.006f, 0.004f, 0.08f, 5), bone, hand, new Vector3((k - 1.5f) * 0.014f, 0, 0), Quaternion.identity, false, "Finger");
                MeshGen.Obj(MeshGen.Sphere(0.03f, 8, 5), bone, a.mU, Vector3.zero, Quaternion.identity, false, "Joint");
                MeshGen.Obj(MeshGen.Sphere(0.022f, 8, 5), bone, a.mL, Vector3.zero, Quaternion.identity, false, "Joint");
                u.localScale = Vector3.one * 1e-4f;          // the flesh and the plate of that arm: gone
                a.Follow();
                return a;
            }
            public void Follow()
            {
                var p0 = up.position; var da = up.rotation * dU; var p1 = p0 + da * lU; var db = lo.rotation * dL; var p2 = p1 + db * lL;
                mU.position = p0; mU.rotation = Quaternion.FromToRotation(Vector3.up, da);
                mL.position = p1; mL.rotation = Quaternion.FromToRotation(Vector3.up, db);
                mH.position = p2; mH.rotation = Quaternion.FromToRotation(Vector3.up, db);
            }
        }
        // ---- Yorick in the pocket: the skull (KayKit, CC0) with a jaw of its own, filmed in a corner of the studio ----
        GameObject skullRig; Transform skullJaw, skullHead; Camera skullCam; AsciiRenderer skullAscii;
        static readonly Vector3 SKULL_AT = STUDIO + new Vector3(40, 0, 0);
        void SkullCam(bool on, float cellPx)
        {
            if (!on || !sHasSkull) { if (skullAscii != null) skullAscii.SetEnabled(false); if (skullRig != null && skullRig.activeSelf) skullRig.SetActive(false); return; }
            if (skullRig == null)
            {
                skullRig = new GameObject("YorickRig"); skullRig.transform.position = SKULL_AT;
                skullHead = new GameObject("Head").transform; skullHead.SetParent(skullRig.transform, false);
                // a real skull (BodyParts3D, DBCLS), its lower jaw cut off at the hinge so it can speak
                var hinge = new Vector3(0, -0.127f, 0.147f) * 0.22f;
                KKObj("yskull", skullHead, Vector3.zero, 0.22f, Quaternion.identity);
                skullJaw = new GameObject("Jaw").transform; skullJaw.SetParent(skullHead, false); skullJaw.localPosition = hinge;
                KKObj("yjaw", skullJaw, -hinge, 0.22f, Quaternion.identity);
                // black in the eye sockets and inside the mouth
                foreach (float ex in new[] { -0.17f, 0.17f })
                { var e = MeshGen.Obj(MeshGen.Sphere(0.085f * 0.22f, 10, 8), actorMats["DARK"], skullHead, new Vector3(ex, 0.07f, 0.62f) * 0.22f, Quaternion.identity, false, "Eye"); e.transform.localScale = new Vector3(1, 0.85f, 0.7f); }
                { var mo = MeshGen.Obj(MeshGen.Sphere(0.16f * 0.22f, 10, 8), actorMats["DARK"], skullHead, new Vector3(0, -0.25f, 0.58f) * 0.22f, Quaternion.identity, false, "Mouth"); mo.transform.localScale = new Vector3(1.3f, 0.45f, 0.9f); }
                // and dark all through inside: only the outer shell carries characters
                { var cav = MeshGen.Obj(MeshGen.Sphere(0.5f * 0.22f, 14, 10), actorMats["DARK"], skullHead, new Vector3(0, 0.08f, -0.12f) * 0.22f, Quaternion.identity, false, "Inside"); cav.transform.localScale = new Vector3(0.86f, 0.92f, 1.32f); }
                var noseD = MeshGen.Obj(MeshGen.Sphere(0.05f * 0.22f, 8, 6), actorMats["DARK"], skullHead, new Vector3(0, -0.07f, 0.66f) * 0.22f, Quaternion.identity, false, "Nose"); noseD.transform.localScale = new Vector3(0.8f, 1.2f, 0.6f);
                GlyphLayer.Build(skullRig);
                var lg = new GameObject("SkullKey"); lg.transform.SetParent(skullRig.transform, false); lg.transform.localPosition = new Vector3(0.5f, 0.6f, 0.9f);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 4; l.intensity = 2.2f; l.color = Mats.Hex(0xffe6c8);
                skullCam = new GameObject("SkullCamera").AddComponent<Camera>(); skullCam.transform.SetParent(skullRig.transform, false);
                skullCam.nearClipPlane = 0.05f; skullCam.farClipPlane = 6; skullCam.fieldOfView = 30; skullCam.allowMSAA = false; skullCam.allowHDR = false;
                skullCam.transform.localPosition = new Vector3(0, 0.02f, 0.62f); skullCam.transform.LookAt(SKULL_AT);
                skullAscii = new AsciiRenderer(skullCam) { surfaceGlyphs = true, actorBoost = 1 };
            }
            skullRig.SetActive(true); skullAscii.SetEnabled(true);
            // it looks about; the jaw clacks when Yorick speaks, and now and then on its own
            float t = Time.unscaledTime;
            skullHead.localRotation = Quaternion.Euler(6 * Mathf.Sin(t * 0.7f), 28 * Mathf.Sin(t * 0.45f), 4 * Mathf.Sin(t * 0.9f));
            bool talking = (sDir != null && sDir.line != null && sDir.line.who == "Череп") || yorickTalkT > 0;
            float open = talking ? (0.5f + 0.5f * Mathf.Sin(t * 13f)) * 16f : (Mathf.PerlinNoise(t * 0.8f, 3.3f) > 0.72f ? (0.5f + 0.5f * Mathf.Sin(t * 11f)) * 9f : 0);
            skullJaw.localRotation = Quaternion.Euler(open, 0, 0);
            var r = SkullRect(PocketProjRect(Screen.width, Screen.height));
            skullCam.aspect = 1;
            skullAscii.exposure = ascii.exposure; skullAscii.bloom = ascii.bloom; skullAscii.outline = ascii.outline; skullAscii.litDiv = ascii.litDiv; skullAscii.depthRef = 0.75f;
            skullAscii.depthRef = 0.62f;
            skullAscii.SetSize(Mathf.Max(16, Mathf.RoundToInt(r.width)), Mathf.Max(16, Mathf.RoundToInt(r.height)), cellPx * 0.7f);       // (finer characters: a sharper skull)
            skullAscii.SelfCheck();
        }
        Rect SkullRect(Rect lp) { float r = lp.width * 0.27f * 1.2f; return new Rect(Mathf.Max(safe.x + 4 * k, lp.x - r * 1.55f), Screen.height * 0.10f, 2 * r, 2 * r); }      // (10 % from the top)
        // ================= saving =================
        float autoT, lastAuto = -99; bool autoAfterScene;
        void AutoSave()
        {
            if (!story || !world || hp <= 0 || Time.time - lastAuto < 3f) return;
            lastAuto = Time.time; SaveGame(AUTO);
        }
        /// what the story keeps in a save: the scenes seen, the choices made, the dead, the chests opened, the skull
        void StorySaveInto(SaveData d)
        {
            if (smap == null) return;
            d.storyDone = new List<string>(sDone).ToArray(); d.storyFlags = new List<string>(sFlags).ToArray();
            var dead = new List<string>(); foreach (var f in foes) if (f.Dead) dead.Add(f.id); d.deadFoes = dead.ToArray();
            var op = new List<string>(); foreach (var c in chests) if (c.open) op.Add(c.id); d.openChests = op.ToArray();
            d.skull = sHasSkull; d.yorickIdle = yorickIdleN;
        }
        void StoryLoadFrom(SaveData d)
        {
            try { if (tavern == null) SetupWorld(); if (smap == null) BuildStory(); else ResetStory(); }
            catch (System.Exception e) { Debug.LogException(e); ShowToast("Не удалось собрать локацию"); return; }
            inventory.Clear(); if (d.items != null) inventory.AddRange(d.items);
            if (d.storyDone != null) foreach (var x in d.storyDone) sDone.Add(x);
            if (d.storyFlags != null) foreach (var x in d.storyFlags) sFlags.Add(x);
            if (d.deadFoes != null) foreach (var f in foes) if (System.Array.IndexOf(d.deadFoes, f.id) >= 0) { f.hp = 0; f.gone = true; f.ch.go.SetActive(false); }
            if (d.openChests != null) foreach (var c in chests) if (System.Array.IndexOf(d.openChests, c.id) >= 0) { c.open = true; c.openT = 1; }
            sHasSkull = d.skull; if (sHasSkull && skullGo != null) skullGo.SetActive(false);
            yorickIdleN = d.yorickIdle;
            StartStory(false);
        }
        /// a new game: the story from its start, a new hero to make (the saves in the slots stay)
        void NewGame()
        {
            if (smap != null) ResetStory();
            sLast = null; gameClock = Sky.DAY_S * 0.585f; inventory.Clear(); RestoreBody();
            yorickIdleN = 0; yorickFirstLimb = false; yorickLost2.Clear(); autoT = 0;
            PlayerPrefs.SetInt("char_made", 0); PlayerPrefs.Save();
            inMenu = false; menuPage = 0;
        }

        // ================= Yorick talks (only while he is with the hero) =================
        static readonly string[] Y_FIGHT = {
            "Я всё понимаю - сражения, кровь, мясо, веселье. Но ты бы не мог поменьше меня трясти.",
            "Давай! Да! Ура! Даа! Давай!",
            "Смотри как у него кишки вывалились.",
            "Я когда-то тоже так мечом махал, пока мне... Ладно, не важно." };
        static readonly string[] Y_HURT = {
            "Ты будешь его бить, не?",
            "Ты если хочешь умереть, сразу скажи, я не буду к тебе привязываться.",
            "Ты точно рыцарь? Где вас таких учат?" };
        // per limb (1 left arm, 2 right arm, 3 left leg, 4 right leg): the first piece he ever loses; a piece lost for good
        static readonly string[] Y_LOST1 = { null, "Ты решил стать пиратом? Крюк есть?", "О, мне как-то раз тоже руку отрезали.", "Одна нога здесь, другая там.", "Забавно ты прыгаешь. Научишь?" };
        static readonly string[] Y_LOST2 = { null, "Всё-таки идея с пиратом понравилась?", "Ээээх... как же ты ночью-то будешь...", "Ножки в одну кучку, тушки в другую...", "Помнится, кто-то обещал меня научить так же прыгать..." };
        static readonly string[] Y_IDLE = {
            "Помнится, мы куда-то собирались. Не подскажешь?",
            "Ты пытаешься карту зарисовать или просто любуешься пейзажами?",
            "Я понял, ты решил бегать по вечерам! Молодец, так держать!.. А теперь, пошли уже отсюда.",
            "Ладно, если тебе так понравилось здесь, можешь дальше тут побегать, а я пока вздремну... Только сильно не тряси, меня укачивает." };
        float yorickCd, yorickIdleT; int yorickIdleN; bool yorickFirstLimb; readonly HashSet<string> yorickLost2 = new HashSet<string>();
        /// a line of Yorick's: over the hero's head (he is in the pocket), voiced when there is a voice for it
        void YorickSay(string text)
        {
            if (!sHasSkull || !story || string.IsNullOrEmpty(text)) return;
            barks.Add(new BarkLine { at = hero.go.transform, who = "Череп", text = text, dur = 2.2f + 0.055f * text.Length });
            yorickTalkT = 1.2f + 0.06f * text.Length; yorickCd = 8;
            PlayVoice("Череп", text);
        }
        float yorickTalkT;
        /// the hero swings at something (in a fight)
        void StoryFight()
        {
            mind.Swing(Time.time);
            if (!story || yorickCd > 0 || NearestEntity(5f) == null) return;
            if (Random.value < 0.4f) YorickSay(Y_FIGHT[Random.Range(0, Y_FIGHT.Length)]); else yorickCd = 4;
        }
        void StoryHurt()
        {
            if (!story || yorickCd > 0) return;
            if (Random.value < 0.25f) YorickSay(Y_HURT[Random.Range(0, Y_HURT.Length)]); else yorickCd = 3;
        }
        void StoryLimbLost(int l, bool second)
        {
            if (!story || l == 0) return;
            if (!second && !yorickFirstLimb) { yorickFirstLimb = true; yorickCd = 0; YorickSay(Y_LOST1[l]); return; }
            if (second && loc != null && yorickLost2.Add(loc.id + ":" + l)) { yorickCd = 0; YorickSay(Y_LOST2[l]); }
        }
        /// wandering about a place that is quiet now: every two minutes or so, in order, each once
        void StoryIdleTalk(float dt)
        {
            yorickCd -= dt; yorickTalkT -= dt;
            var L = loc as StoryLocation; if (L == null || !sHasSkull || yorickIdleN >= Y_IDLE.Length) return;
            bool hostile = false; foreach (var f in foes) if (f.L == L && !f.Dead && f.kind != "drunk") { hostile = true; break; }
            bool fighting = player.combatT > 0 || player.Attacking;
            bool moving = player.Velocity.sqrMagnitude > 0.3f;
            if (hostile || fighting || sDir != null) { yorickIdleT = 0; return; }
            if (moving) yorickIdleT += dt;
            if (yorickIdleT > 120f) { yorickIdleT = 0; YorickSay(Y_IDLE[yorickIdleN++]); }
        }

        // ================= voices (made with Voicebox, see tools/voicebox_voices.py): Resources/Voice/<key>.ogg =================
        AudioSource voiceSrc; readonly Dictionary<string, AudioClip> voiceCache = new Dictionary<string, AudioClip>();
        public static string VoiceKey(string who, string text)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var h = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes((who ?? "") + "|" + text));
                var sb = new System.Text.StringBuilder(); for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2")); return sb.ToString();
            }
        }
        void PlayVoice(string who, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string key = VoiceKey(who, text); AudioClip c;
            if (!voiceCache.TryGetValue(key, out c)) { c = Resources.Load<AudioClip>("Voice/" + key); voiceCache[key] = c; }
            if (c == null) return;
            if (voiceSrc == null) { voiceSrc = gameObject.AddComponent<AudioSource>(); voiceSrc.playOnAwake = false; voiceSrc.spatialBlend = 0; }
            voiceSrc.Stop(); voiceSrc.clip = c; voiceSrc.volume = setSound / 100f; voiceSrc.Play();
        }

        // ================= the bestiary: who has been met (and beaten), as text; after D&D 5e's monsters =================
        class Beast { public string id, name, basis, look, ways, stats; }
        static readonly Beast[] BEASTS = {
            new Beast { id = "rat", name = "Крысолюд", basis = "по образу ратфолка (Tome of Beasts) и крысиного племени D&D",
                look = "Невысокий, сутулый, весь в серо-бурой шерсти; длинная морда, голый хвост. Рваный капюшон, кинжал. Бегает на четвереньках.",
                ways = "Бродит по норам, роется в хламе, оглядывается. Видит только то, что перед ним, но слышит шаги рядом. Заметив чужого — пищит и бежит на него на четвереньках. Бьёт и бежит: подскакивает (иногда с укусом в прыжке), режет кинжалом и сразу отпрыгивает, кружит на расстоянии и снова бросается.",
                stats = "Скорость: быстрая · Здоровье: низкое · Атаки: кинжал, укус в прыжке" },
            new Beast { id = "boss", name = "Большая Крыса", basis = "по образу оборотня-крысы (вэрэрат, Monster Manual)",
                look = "На голову выше других крысолюдов, чёрная шерсть, красные глаза. Шипастый железный шлем, меховой плащ, две сабли, ножи за поясом.",
                ways = "Чует добычу носом даже за стеной и учится по ходу боя: кто бьёт часто и одинаково, того ловит на две сабли и сразу отвечает; кто держит блок — того обходит кругом; кто рвётся в одну сторону — там его и встречает; кто держится подальше — получает нож туда, куда шагнёт. Замах у него короткий и тихий. Бьёт сериями, отпрыгивает в сторону и назад. Раненый свирепеет.",
                stats = "Скорость: очень быстрая · Здоровье: среднее · Атаки: серии двумя саблями, укус, брошенный нож, парирование с ответом, блок" },
            new Beast { id = "zombie", name = "Воин-зомби", basis = "по образу зомби (Monster Manual)",
                look = "Мёртвый рыцарь: латы с прорехами, голый череп с отвисшей челюстью, рука — одни кости.",
                ways = "Медлителен и упрям. Бредёт к живому, бьёт тяжело и редко. Нежитская стойкость: смертельный удар его не всегда валит — иногда он поднимается ещё раз.",
                stats = "Скорость: медленная · Здоровье: среднее · Атаки: удар мечом" },
            new Beast { id = "drunk", name = "Пьяный крысолюд", basis = "крысолюд вне службы",
                look = "Тот же крысолюд, только в рубахе и без меча; покачивается и машет лапами.",
                ways = "Сидит в крысином трактире и болтает. Сам не нападает; ударишь — вспомнит, что он крысолюд.",
                stats = "Мирный, пока его не трогают" },
        };
        readonly HashSet<string> seenBeasts = new HashSet<string>(), beatenBeasts = new HashSet<string>(); bool bestLoaded; float bestScroll;
        void BestLoad() { if (bestLoaded) return; bestLoaded = true; foreach (var x in PlayerPrefs.GetString("best_seen", "").Split(',')) if (x.Length > 0) seenBeasts.Add(x); foreach (var x in PlayerPrefs.GetString("best_beaten", "").Split(',')) if (x.Length > 0) beatenBeasts.Add(x); }
        void BestMark(string kind, bool beaten)
        {
            BestLoad(); bool ch = seenBeasts.Add(kind); if (beaten) ch |= beatenBeasts.Add(kind);
            if (ch) { PlayerPrefs.SetString("best_seen", string.Join(",", seenBeasts)); PlayerPrefs.SetString("best_beaten", string.Join(",", beatenBeasts)); PlayerPrefs.Save(); if (seenBeasts.Count > 0 && !beaten) ShowToast("Бестиарий: " + BeastName(kind)); }
        }
        static string BeastName(string id) { foreach (var b in BEASTS) if (b.id == id) return b.name; return id; }
        GUIStyle sBestT, sBestB;
        void BestiaryUI(float x0, float y0, float pw, float bottom)
        {
            BestLoad();
            if (sBestB == null || sBestB.fontSize != Mathf.RoundToInt(11 * k)) { sBestB = new GUIStyle(sBadge) { wordWrap = true, fontSize = Mathf.RoundToInt(11 * k), alignment = TextAnchor.UpperLeft }; sBestT = new GUIStyle(sBadge) { fontSize = Mathf.RoundToInt(14 * k), alignment = TextAnchor.UpperLeft }; }
            GUI.Label(new Rect(x0, y0, pw, 22 * k), "БЕСТИАРИЙ · открыто " + seenBeasts.Count + " из " + BEASTS.Length, sTitle);
            var area = new Rect(x0, y0 + 28 * k, pw, bottom - y0 - 34 * k); uiRects.Add(area);
            foreach (var pt in InputHub.active.Values) if (pt.owner == "ui" && area.Contains(pt.start)) bestScroll = Mathf.Max(0, bestScroll - pt.Delta.y);
            GUI.BeginGroup(area); float y = -bestScroll;
            foreach (var b in BEASTS)
            {
                bool open = seenBeasts.Contains(b.id), won = beatenBeasts.Contains(b.id);
                if (!open) { GUI.Label(new Rect(0, y, pw, 20 * k), "??? — ещё не встречен", sBestT); y += 30 * k; continue; }
                GUI.Label(new Rect(0, y, pw, 20 * k), b.name + (won ? "  · побеждён" : ""), sBestT); y += 20 * k;
                string txt = b.look + "\n" + b.ways + "\n" + b.stats + "\n(" + b.basis + ")";
                float h = sBestB.CalcHeight(new GUIContent(txt), pw); GUI.Label(new Rect(0, y, pw, h), txt, sBestB); y += h + 14 * k;
            }
            GUI.EndGroup();
        }
        string StoryTitle() { var L = loc as StoryLocation; return L != null ? L.TitleAt(pos) : null; }
    }
}
