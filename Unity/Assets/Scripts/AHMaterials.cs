// Material palette. Every material carries an ASCII id (_AsciiId) that the glyph shader maps to a character ramp.
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public static class MatId
    {
        public const int SKIN = 0, HAIR = 1, LINEN = 2, PANTS = 3, LEATHER = 4, CHAIN = 5, PLATE = 6, STEEL = 7, WOOD = 8, GOLD = 9,
            GLOW = 10, EYE = 11, DARK = 12, RED = 13, ROBE = 14, ROBED = 15, STONE = 16, LEATHER2 = 17, HAIR2 = 18,
            PLANK = 19, BEAM = 20, PLASTER = 21, HEARTH = 22, FIRE = 23, MOON = 24, GLASS = 25, GLASS2 = 26, GLASS3 = 27, FLOOR = 28,
            SHADOW = 29, GROUND = 30, LEAF = 31, ROCK = 32, WATER = 33,
            CAVEFLOOR = 34, WETROCK = 35, MUD = 36, MOSS = 37, BONE = 38, MUSH = 39, GLOWSHROOM = 40,
            BARK = 41, BIRCH = 42, WHEAT = 43, FLOWERB = 44, FLOWERP = 45, HAY = 46, ROOF = 47, WINDOW = 48, FLOWERW = 49, ROAD = 50,
            AUTUMN = 51, AUTUMN2 = 52, SNOW = 53, MARBLE = 54, TOXIC = 55, ICE = 56, CRYSTAL = 57, CRYSTALB = 58, SAND = 59, COBBLE = 60;
        // material id -> glyph ramp (same table as the web build)
        public static readonly int[] RAMP = {
            0, 1, 2, 2, 3, 4, 5, 6, 7, 8, 9, 9, 10, 2, 2, 2, 11, 3, 1,
            7, 7, 2, 11, 9, 9, 6, 6, 6, 11,
            13, 11, 1, 11, 11,
            11, 11, 10, 1, 2, 4, 9,
            7, 7, 1, 9, 9, 11, 2, 2, 9, 11,
            1, 1, 11, 11, 9, 6, 9, 9, 11, 11 };
    }

    public class MatDef
    {
        public int id; public uint color; public float rough, metal; public uint emissive; public bool lambert, twoSided;
        public MatDef(int id, uint color, float rough, float metal, uint emissive = 0, bool lambert = false, bool twoSided = false)
        { this.id = id; this.color = color; this.rough = rough; this.metal = metal; this.emissive = emissive; this.lambert = lambert; this.twoSided = twoSided; }
    }

    public class Mats
    {
        public static readonly Dictionary<string, MatDef> DEFS = new Dictionary<string, MatDef>
        {
            { "SKIN", new MatDef(MatId.SKIN, 0xe0a882, 0.6f, 0) },
            { "HAIR", new MatDef(MatId.HAIR, 0x5a3418, 0.5f, 0) },
            { "HAIR2", new MatDef(MatId.HAIR2, 0x6a3c1a, 0.5f, 0, 0, false, true) },
            { "LINEN", new MatDef(MatId.LINEN, 0xcdbb92, 0.9f, 0) },
            { "PANTS", new MatDef(MatId.PANTS, 0x5c5046, 0.9f, 0) },
            { "LEATHER", new MatDef(MatId.LEATHER, 0x8f5a2e, 0.6f, 0) },
            { "LEATHER2", new MatDef(MatId.LEATHER2, 0x5e3a1e, 0.6f, 0) },
            { "CHAIN", new MatDef(MatId.CHAIN, 0x9aa2ae, 0.45f, 0.8f) },
            { "PLATE", new MatDef(MatId.PLATE, 0x8e96a4, 0.34f, 0.85f) },
            { "STEEL", new MatDef(MatId.STEEL, 0xb8c2cf, 0.25f, 1.0f) },
            { "WOOD", new MatDef(MatId.WOOD, 0x7a4f2a, 0.8f, 0) },
            { "GOLD", new MatDef(MatId.GOLD, 0xe8b443, 0.3f, 1.0f) },
            { "GLOW", new MatDef(MatId.GLOW, 0x70e0ff, 0.2f, 0, 0x60d8ff) },
            { "EYE", new MatDef(MatId.EYE, 0xf2e8d8, 0.2f, 0, 0x332a20) },
            { "DARK", new MatDef(MatId.DARK, 0x101014, 1, 0) },
            { "RED", new MatDef(MatId.RED, 0xb02828, 0.85f, 0) },
            { "ROBE", new MatDef(MatId.ROBE, 0x3f4fb8, 0.85f, 0, 0, false, true) },
            { "CAPE", new MatDef(MatId.RED, 0xa8343c, 0.75f, 0, 0, false, true) },            // the knight's cloak
            { "ROBED", new MatDef(MatId.ROBED, 0x28306e, 0.9f, 0) },
            { "PLANK", new MatDef(MatId.PLANK, 0xffffff, 0.85f, 0) },
            { "FLOOR", new MatDef(MatId.FLOOR, 0x5e5044, 1, 0, 0, true) },
            { "BEAM", new MatDef(MatId.BEAM, 0x4a2e18, 0.9f, 0) },
            { "PLASTER", new MatDef(MatId.PLASTER, 0xb8a888, 1, 0, 0, true) },
            { "HEARTH", new MatDef(MatId.HEARTH, 0xffffff, 1, 0, 0, true) },
            { "FIRE", new MatDef(MatId.FIRE, 0xffa040, 1, 0, 0xff7a20) },
            { "MOON", new MatDef(MatId.MOON, 0x4060a0, 1, 0, 0x2a4a90) },
            { "GLASS", new MatDef(MatId.GLASS, 0x3a7a3a, 0.15f, 0.3f, 0x0a200a) },
            { "GLASS2", new MatDef(MatId.GLASS2, 0x7a3a18, 0.15f, 0.3f, 0x200a04) },
            { "GLASS3", new MatDef(MatId.GLASS3, 0x6a6aa8, 0.15f, 0.3f, 0x101028) },
            { "SHADOW", new MatDef(MatId.SHADOW, 0x07050d, 1, 0, 0, true) },     // the hero's body: living darkness
            { "GROUND", new MatDef(MatId.GROUND, 0x1c3319, 1, 0, 0, true) },
            { "LEAF", new MatDef(MatId.LEAF, 0x2f6b2f, 1, 0, 0, true) },
            { "ROCK", new MatDef(MatId.ROCK, 0x6c6c72, 1, 0, 0, true) },
            { "WATER", new MatDef(MatId.WATER, 0x0c1830, 1, 0, 0x02040a, true) },
            // the cave
            { "CAVEFLOOR", new MatDef(MatId.CAVEFLOOR, 0x3a3836, 1, 0, 0, true) },
            { "WETROCK", new MatDef(MatId.WETROCK, 0x4a4a52, 0.3f, 0.15f) },          // wet stone: a sheen where the light falls
            { "MUD", new MatDef(MatId.MUD, 0x2e2216, 1, 0, 0, true) },
            { "MOSS", new MatDef(MatId.MOSS, 0x3f6a2c, 1, 0, 0, true) },
            { "BONE", new MatDef(MatId.BONE, 0xd8cfb4, 0.8f, 0) },
            { "MUSH", new MatDef(MatId.MUSH, 0x8a5e3c, 0.9f, 0) },
            { "GLOWSHROOM", new MatDef(MatId.GLOWSHROOM, 0x5ff0d0, 0.5f, 0, 0x3fd8b8) },     // the lake (its characters come from the water shader)
            { "STONE", new MatDef(MatId.STONE, 0xffffff, 0.95f, 0, 0, true) },
            { "BARK", new MatDef(MatId.BARK, 0x4a3424, 1, 0, 0, true) },
            { "BIRCH", new MatDef(MatId.BIRCH, 0xc8c8bc, 1, 0, 0, true) },
            { "WHEAT", new MatDef(MatId.WHEAT, 0xc8a24a, 1, 0, 0, true, true) },
            { "FLOWERB", new MatDef(MatId.FLOWERB, 0x3a6ad8, 1, 0, 0x0a1430, true, true) },
            { "FLOWERP", new MatDef(MatId.FLOWERP, 0xd84a8a, 1, 0, 0x200814, true, true) },
            { "FLOWERW", new MatDef(MatId.FLOWERW, 0xeeeee0, 1, 0, 0x181818, true, true) },
            { "HAY", new MatDef(MatId.HAY, 0xb08a3a, 1, 0, 0, true) },
            { "ROOF", new MatDef(MatId.ROOF, 0x4a3a40, 1, 0, 0, true) },
            { "WINDOW", new MatDef(MatId.WINDOW, 0xffb860, 1, 0, 0xffa040) },          // warm light in the windows at night
            { "ROAD", new MatDef(MatId.ROAD, 0x3a2e22, 1, 0, 0, true) },
            // the other biomes
            { "AUTUMN", new MatDef(MatId.AUTUMN, 0xc0581e, 1, 0, 0x140600, true, true) },        // autumn leaves, rust and red
            { "AUTUMN2", new MatDef(MatId.AUTUMN2, 0xd8a028, 1, 0, 0x140c00, true, true) },      // and gold
            { "SNOW", new MatDef(MatId.SNOW, 0xdfe6ee, 1, 0, 0x06080c, true) },
            { "MARBLE", new MatDef(MatId.MARBLE, 0xd6d0c2, 1, 0, 0, true) },                    // the bright city's white stone
            { "TOXIC", new MatDef(MatId.TOXIC, 0x62e03a, 1, 0, 0x2a8a14) },                    // acid ooze: it glows
            { "ICE", new MatDef(MatId.ICE, 0x9cc4dc, 0.15f, 0.2f, 0x0c1620) },
            { "CRYSTAL", new MatDef(MatId.CRYSTAL, 0xb27aff, 0.2f, 0.1f, 0x7a30e0) },          // the mines' crystals, violet
            { "CRYSTALB", new MatDef(MatId.CRYSTALB, 0x6ad0ff, 0.2f, 0.1f, 0x2a88d8) },         // and blue
            { "SAND", new MatDef(MatId.SAND, 0x9c8058, 1, 0, 0, true) },
            { "COBBLE", new MatDef(MatId.COBBLE, 0x58544e, 1, 0, 0, true) },
        };

        public static Color Hex(uint c) { return new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, 1); }

        readonly Dictionary<string, Material> map = new Dictionary<string, Material>();
        public readonly bool actor;
        public Material this[string k] { get { return map[k]; } }

        public Mats(bool actor)
        {
            this.actor = actor;
            var sh = Shader.Find("AsciiHero/Lit");
            if (sh == null) { Debug.LogError("AsciiHero/Lit shader not found (is it in Always Included Shaders?)"); sh = Shader.Find("Standard"); }
            foreach (var kv in DEFS)
            {
                var d = kv.Value; var m = new Material(sh) { name = kv.Key + (actor ? "_actor" : "") };
                m.SetColor("_Color", Hex(d.color));
                m.SetFloat("_Glossiness", d.lambert ? 0f : 1f - d.rough);
                m.SetFloat("_Metallic", d.lambert ? 0f : d.metal);
                m.SetColor("_EmissionColor", Hex(d.emissive));
                m.SetFloat("_Cull", d.twoSided ? 0f : 2f);
                m.SetFloat("_AsciiId", AsciiIdValue(d.id, actor));
                // characters built on this surface: ramp row, glyph size in metres (finer on characters than on walls)
                m.SetFloat("_AsciiRow", d.id < MatId.RAMP.Length ? MatId.RAMP[d.id] : 0);
                m.SetFloat("_AsciiActor", actor ? 1 : 0);
                bool big = kv.Key == "FLOOR" || kv.Key == "PLASTER" || kv.Key == "PLANK" || kv.Key == "HEARTH" || kv.Key == "STONE" || kv.Key == "BEAM";
                m.SetFloat("_GlyphSmoke", kv.Key == "SHADOW" ? 1 : 0);
                m.SetFloat("_Cloth", actor && (kv.Key == "ROBE" || kv.Key == "ROBED" || kv.Key == "CAPE") ? 1 : kv.Key == "LEAF" || kv.Key == "AUTUMN" || kv.Key == "AUTUMN2" || kv.Key == "WHEAT" || kv.Key == "FLOWERB" || kv.Key == "FLOWERP" || kv.Key == "FLOWERW" ? 2 : 0);      // 2: leaves, swaying in the wind
                m.SetFloat("_AsciiFlat", kv.Key == "GROUND" || kv.Key == "FLOOR" || kv.Key == "WATER" || kv.Key == "CAVEFLOOR" || kv.Key == "MUD" || kv.Key == "ROAD" || kv.Key == "SAND" || kv.Key == "COBBLE" ? 1 : 0);        // floors and walls get no outline
                if (kv.Key == "GROUND" || kv.Key == "CAVEFLOOR" || kv.Key == "MUD" || kv.Key == "ROAD" || kv.Key == "SAND" || kv.Key == "COBBLE") m.SetVector("_GlyphCell", new Vector4(0.11f, 0.18f, 0, 0)); else
                if (kv.Key == "WETROCK") m.SetVector("_GlyphCell", new Vector4(0.07f, 0.115f, 0, 0)); else
                if (kv.Key == "BONE") m.SetVector("_GlyphCell", new Vector4(0.022f, 0.036f, 0, 0)); else       // thin real bones: finer characters
                m.SetVector("_GlyphCell", actor ? new Vector4(0.0135f, 0.0222f, 0, 0) : big ? new Vector4(0.06f, 0.1f, 0, 0) : new Vector4(0.034f, 0.056f, 0, 0));
                map[kv.Key] = m;
            }
        }
        /// a copy recoloured (the ground of another biome, a wall of another stone); cached by key and colour
        readonly Dictionary<string, Material> tints = new Dictionary<string, Material>();
        public Material Tint(string k, uint color, uint emission = 0)
        {
            string key = k + color.ToString("x6") + emission.ToString("x6"); Material m;
            if (tints.TryGetValue(key, out m)) return m;
            m = new Material(map[k]) { name = k + "_" + color.ToString("x6") }; m.SetColor("_Color", Hex(color)); m.SetColor("_EmissionColor", Hex(emission));
            tints[key] = m; return m;
        }
        /// a copy with coarser characters (cells 2.4 times as big, near the ground's): for big things - houses, towers, cliffs - whose
        /// surfaces would otherwise carry hundreds of thousands of characters each
        readonly Dictionary<Material, Material> bigs = new Dictionary<Material, Material>();
        public Material Big(Material src) { return Coarse(src, 2.4f, bigs); }
        /// (trees: trunks and branches, logs and stumps - 1.6 times as big)
        readonly Dictionary<Material, Material> mids = new Dictionary<Material, Material>();
        public Material Mid(Material src) { return Coarse(src, 1.6f, mids); }
        Material Coarse(Material src, float f, Dictionary<Material, Material> cache)
        {
            if (src == null || !src.HasProperty("_GlyphCell")) return src; Material m;
            if (cache.TryGetValue(src, out m) && m != null) return m;
            m = new Material(src) { name = src.name + "_x" + f }; var c = src.GetVector("_GlyphCell");
            m.SetVector("_GlyphCell", new Vector4(Mathf.Max(c.x, 0.034f) * f, Mathf.Max(c.y, 0.056f) * f, c.z, c.w));
            cache[src] = m; return m;
        }
        public static float AsciiIdValue(int id, bool actor) { return (id + 1 + (actor ? 128 : 0)) / 255f; }
        /// a copy with its own texture (tavern props)
        public Material WithTexture(string k, Texture tex, Vector2 tiling)
        {
            var m = new Material(map[k]); m.mainTexture = tex; m.mainTextureScale = tiling; return m;
        }
    }
}
