using UnityEngine;

namespace AsciiHero
{
    // Owns armour integrity. Game retains orchestration of effects, animation and save DTOs.
    internal sealed class BodyIntegrity
    {
        internal const int PieceCount = 5;
        internal int Health { get; private set; } = 100;
        // Stable arrays retained for existing animation and save consumers.
        internal bool[] Present { get; } = new bool[] { true, true, true, true, true };
        internal int[] Strength { get; } = new int[] { 100, 100, 100, 100, 100 };
        internal bool[] Broken { get; } = new bool[PieceCount];
        static readonly float[] HitWeights = { 0.15f, 0.25f, 0.25f, 0.175f, 0.175f };

        internal int Count()
        {
            int n = 0;
            foreach (bool present in Present) if (present) n++;
            return n;
        }

        internal void Recalculate()
        {
            int sum = 0;
            for (int i = 0; i < PieceCount; i++) if (Present[i]) sum += Strength[i];
            Health = Count() == 0 ? 0 : Mathf.Max(1, Mathf.CeilToInt(sum / 5f));
        }

        internal int SelectHit()
        {
            float total = 0;
            for (int i = 0; i < PieceCount; i++) if (Present[i]) total += HitWeights[i];
            if (total <= 0) return -1;
            float remaining = Random.value * total;
            int hit = 0;
            for (int i = 0; i < PieceCount; i++)
            {
                if (!Present[i]) continue;
                hit = i;
                remaining -= HitWeights[i];
                if (remaining <= 0) break;
            }
            return hit;
        }

        // Recalculation is explicit to preserve the existing callback order.
        internal bool ApplyDamage(int hit, int percent)
        {
            Strength[hit] = Mathf.Max(0, Strength[hit] - percent * 4);
            if (Strength[hit] > 0) return false;
            Present[hit] = false;
            return true;
        }

        internal void MarkBroken(int hit) => Broken[hit] = true;

        internal void RestorePiece(int index, bool replacement)
        {
            Present[index] = true;
            Strength[index] = 100;
            if (replacement) Broken[index] = false;
        }

        internal void Reset()
        {
            Health = 100;
            for (int i = 0; i < PieceCount; i++) RestorePiece(i, true);
        }

        // Preserve legacy save fallback and stored aggregate health exactly.
        internal void Load(int health, bool[] present, int[] strength, bool[] broken)
        {
            for (int i = 0; i < PieceCount; i++)
                Present[i] = present == null || i >= present.Length || present[i];
            Health = Mathf.Clamp(health, 0, 100);
            if (Health <= 0)
            {
                Health = 100;
                for (int i = 0; i < PieceCount; i++) Present[i] = true;
            }
            for (int i = 0; i < PieceCount; i++)
            {
                Strength[i] = strength != null && i < strength.Length ? Mathf.Clamp(strength[i], 0, 100) : (Present[i] ? 100 : 0);
                Broken[i] = broken != null && i < broken.Length && broken[i];
                if (Present[i] && Strength[i] <= 0) Strength[i] = 100;
            }
        }
    }
}
