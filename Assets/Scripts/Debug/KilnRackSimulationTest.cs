using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The Kiln rack and the container sprouted malt travels in: the baskets stand in a
    /// group centred on the rack (so the one a hand takes is where the spotlight points,
    /// and four fit on screen), and before the Turning Vat the sprouted malt has its own
    /// look, not the dry-seed sack and not the vat's wicker basket.
    /// </summary>
    public class KilnRackSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== KILN RACK SIMULATION ===</b></color>");
            float centre = KilnBench.RackSlotX(0, 1);
            bool symmetric = true, spaced = true;
            for (int count = 1; count <= 4; count++)
            {
                float sum = 0f;
                for (int i = 0; i < count; i++)
                {
                    sum += KilnBench.RackSlotX(i, count) - centre;
                    if (i > 0) spaced &= Mathf.Abs(KilnBench.RackSlotX(i, count) - KilnBench.RackSlotX(i - 1, count) - 0.75f) < 1e-4f;
                }
                symmetric &= Mathf.Abs(sum) < 1e-4f;
            }
            Check(symmetric, "rack: for one to four baskets the group is centred on the rack");
            Check(spaced, "rack: the baskets stand 0.75 apart");

            Color32[] sack = Read(MaltArt.Sack()), crate = Read(MaltArt.MaltCrate()), basket = Read(MaltArt.Basket());
            Check(Differ(crate, sack) > 0.25f, $"art: the sprouted-malt crate is not the dry-seed sack ({Differ(crate, sack):P0} of its pixels differ)");
            Check(Differ(crate, basket) > 0.25f, $"art: the sprouted-malt crate is not the vat's wicker basket ({Differ(crate, basket):P0} differ)");
            Check(Sprouts(crate) >= 8 && Sprouts(sack) == 0, $"art: the crate shows sprouts ({Sprouts(crate)} green pixels), the sack none");

            // With the Turning Vat the malt goes to the kiln in a small, low basket: not the
            // tall wicker one, not the sack, not the crate.
            Color32[] small = Read(MaltArt.SmallBasket());
            Check(Differ(small, sack) > 0.25f && Differ(small, crate) > 0.25f,
                $"art: the small basket is neither the sack ({Differ(small, sack):P0}) nor the crate ({Differ(small, crate):P0})");
            Check(DrawnHeight(small) <= DrawnHeight(basket) * 0.6f,
                $"art: the small basket stands at most 60 % of the tall basket's height ({DrawnHeight(small)} px against {DrawnHeight(basket)})");

            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        /// <summary>Rows that hold any opaque pixel.</summary>
        private static int DrawnHeight(Color32[] px)
        {
            int w = 26, rows = 0;
            for (int y = 0; y < px.Length / w; y++)
            {
                bool any = false;
                for (int x = 0; x < w && !any; x++) any = px[y * w + x].a > 0;
                if (any) rows++;
            }
            return rows;
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        /// <summary>A sprite's pixels. Baked sprites are not readable, so it is drawn through a render texture.</summary>
        private static Color32[] Read(Sprite s)
        {
            Texture tex = s.texture;
            int w = tex.width, h = tex.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = read.GetPixels32();
            Destroy(read);
            return px;
        }

        /// <summary>The share of pixels that differ visibly (colour or whether they are drawn at all).</summary>
        private static float Differ(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return 1f;
            int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                bool drawnA = a[i].a > 128, drawnB = b[i].a > 128;
                if (drawnA != drawnB || (drawnA && (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b)) > 40)) n++;
            }
            return n / (float)a.Length;
        }

        /// <summary>Pixels of fresh green: a rootlet.</summary>
        private static int Sprouts(Color32[] px)
        {
            int n = 0;
            foreach (Color32 c in px) if (c.a > 128 && c.g > c.r + 12 && c.g > c.b + 40) n++;
            return n;
        }
    }
}
