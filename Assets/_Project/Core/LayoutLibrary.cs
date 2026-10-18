using System;
using System.Collections.Generic;

namespace Mahjong.Core
{
    /// <summary>Hand-authored silhouettes drawn as ASCII art. '#' is a tile, anything else is empty.</summary>
    public static class LayoutLibrary
    {
        public sealed class Authored
        {
            public string Id;
            public bool Special;
            public bool Small;
            public Layout Layout;
            public int BaseCount;   // tiles on layer 0 (cannot trim below this)
        }

        struct LayerDef
        {
            public string[] Rows; public int Ox, Oy;
            public LayerDef(int ox, int oy, params string[] rows) { Ox = ox; Oy = oy; Rows = rows; }
        }

        static readonly List<Authored> all = new List<Authored>();
        static readonly Dictionary<string, Layout> tutorial = new Dictionary<string, Layout>();

        public static IList<Authored> All { get { return all; } }

        public static Layout Tutorial(int n)
        {
            return tutorial["tut" + n];
        }

        static string[] Rect(int w, int h)
        {
            var rows = new string[h];
            string r = new string('#', w);
            for (int i = 0; i < h; i++) rows[i] = r;
            return rows;
        }

        static Layout Make(string id, string family, params LayerDef[] layers)
        {
            var pos = new List<TilePos>();
            for (int z = 0; z < layers.Length; z++)
            {
                var ld = layers[z];
                for (int r = 0; r < ld.Rows.Length; r++)
                    for (int c = 0; c < ld.Rows[r].Length; c++)
                        if (ld.Rows[r][c] == '#') pos.Add(new TilePos(ld.Ox + c * 2, ld.Oy + r * 2, z));
            }
            return new Layout(id, family, Procedural.Finalize(pos, 144));
        }

        static void Add(string id, bool special, bool small, params LayerDef[] layers)
        {
            var l = Make(id, "authored", layers);
            all.Add(new Authored { Id = id, Special = special, Small = small, Layout = l, BaseCount = l.CountOnLayer(0) });
        }

        static LayoutLibrary()
        {
            // Tutorial boards (levels 1..5).
            tutorial["tut1"] = Make("tut1", "tutorial", new LayerDef(0, 0, Rect(4, 2)), new LayerDef(2, 0, Rect(2, 2)));
            tutorial["tut2"] = Make("tut2", "tutorial", new LayerDef(0, 0, Rect(6, 2)), new LayerDef(2, 0, Rect(4, 1)));
            tutorial["tut3"] = Make("tut3", "tutorial", new LayerDef(0, 0, Rect(6, 3)), new LayerDef(4, 2, Rect(2, 1)));
            tutorial["tut4"] = Make("tut4", "tutorial", new LayerDef(0, 0, Rect(6, 3)), new LayerDef(2, 2, Rect(4, 1)), new LayerDef(4, 2, Rect(2, 1)));
            tutorial["tut5"] = Make("tut5", "tutorial",
                new LayerDef(0, 0, "########", "########", "..####.."), new LayerDef(4, 0, Rect(4, 2)));

            Add("pyramid", false, false,
                new LayerDef(0, 0, Rect(8, 5)), new LayerDef(1, 1, Rect(7, 4)), new LayerDef(2, 2, Rect(6, 3)),
                new LayerDef(3, 3, Rect(5, 2)), new LayerDef(4, 4, Rect(4, 1)));

            Add("pyramid-small", false, true,
                new LayerDef(0, 0, Rect(6, 3)), new LayerDef(1, 1, Rect(5, 2)), new LayerDef(2, 2, Rect(4, 1)));

            Add("turtle", true, false,
                new LayerDef(0, 0,
                    ".############.", "...########...", "..##########..", ".############.",
                    ".############.", "..##########..", "...########...", ".############."),
                new LayerDef(0, 2, "....######....", "....######....", "....######....", "....######....", "....######....", "....######...."),
                new LayerDef(0, 4, ".....####.....", ".....####.....", ".....####.....", ".....####....."),
                new LayerDef(0, 6, "......##......", "......##......"),
                new LayerDef(13, 7, "#"));

            Add("diamond", false, false,
                new LayerDef(0, 0,
                    "......##......", "....######....", "..##########..", "##############",
                    "##############", "..##########..", "....######....", "......##......"),
                new LayerDef(0, 4, ".....####.....", "...########...", "...########...", ".....####....."),
                new LayerDef(0, 6, ".....####.....", ".....####....."));

            Add("cross", true, false,
                new LayerDef(0, 0,
                    ".....####.....", ".....####.....", "##############", "##############",
                    "##############", "##############", ".....####.....", ".....####....."),
                new LayerDef(0, 4, "....######....", "....######....", "....######....", "....######...."),
                new LayerDef(0, 6, ".....####.....", ".....####....."));

            Add("heart", true, false,
                new LayerDef(0, 0,
                    "..####..####..", ".############.", "##############", ".############.",
                    "..##########..", "....######....", "......##......"),
                new LayerDef(0, 2, "..###....###..", "..##########..", "...########...", ".....####....."),
                new LayerDef(0, 4, "....######....", "....######...."));

            Add("butterfly", true, false,
                new LayerDef(0, 0,
                    "####......####", "#####....#####", "######..######", ".############.",
                    "######..######", "#####....#####", "####......####"),
                new LayerDef(0, 2, "..###....###..", "..####..####..", "...########...", "..####..####..", "..###....###.."),
                new LayerDef(0, 6, "....######...."));

            Add("fortress", true, false,
                new LayerDef(0, 0,
                    "##############", "##############", "##..........##", "##..........##",
                    "##..........##", "##..........##", "##############", "##############"),
                new LayerDef(0, 0,
                    "##..........##", "##..........##", "..............", "..............",
                    "..............", "..............", "##..........##", "##..........##"),
                new LayerDef(0, 0,
                    "##..........##", "##..........##", "..............", "..............",
                    "..............", "..............", "##..........##", "##..........##"));

            Add("bridge", true, false,
                new LayerDef(0, 0,
                    "####......####", "####......####", "##############", "##############",
                    "####......####", "####......####"),
                new LayerDef(0, 0,
                    ".##........##.", ".##........##.", ".############.", ".############.",
                    ".##........##.", ".##........##."),
                new LayerDef(0, 4, "....######....", "....######...."));

            Add("hourglass", true, false,
                new LayerDef(0, 0,
                    "##############", ".############.", "..##########..", "....######....",
                    "....######....", "..##########..", ".############.", "##############"),
                new LayerDef(0, 2, "..##########..", "....######....", "......##......",
                    "......##......", "....######....", "..##########.."));

            Add("stairs", false, false,
                new LayerDef(0, 0,
                    "##............", "####..........", "######........", "########......",
                    "##########....", "############..", "##############"),
                new LayerDef(0, 2, "##............", "####..........", "######........", "########......",
                    "##########....", "############.."),
                new LayerDef(0, 4, "##............", "####..........", "######........", "########......",
                    "##########...."));

            all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }
    }
}
