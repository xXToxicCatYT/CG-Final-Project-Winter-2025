using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;

namespace UnityEditor.Rendering.Universal
{
    [ScriptedImporter(0, "cube")]

    class URPCubeLutImporter : ScriptedImporter
    {
        public const int k_MinLutSize = 16;
        public const int k_MaxLutSize = 128;

        // In case PPv2 is still in the project for some reason, exclude these files
        static readonly List<string> s_Excluded = new List<string>()
        {
            "Linear to sRGB r1",
            "Linear to Unity Log r1",
            "sRGB to Linear r1",
            "sRGB to Unity Log r1",
            "Unity Log to Linear r1",
            "Unity Log to sRGB r1"
        };

        public override void OnImportAsset(AssetImportContext ctx)
        {
                bool success = ParseCubeData(ctx, out var size, out var colors);
                if (!success)
                    return;
            if (success)
            {
                var width      = size * size;
                var colorCount = colors.Length;

                var t2d = new Texture2D(width, size, TextureFormat.RGBAFloat, false);

                var tempColors = new Color[colorCount];

                for (var i = 0; i < colorCount; i++)
                {
                    // 1D to 3D Coords
                    var X = i % size;
                    var Y = (i / size) % size;
                    var Z = i / (size * size);

                    // 2D Coords
                    // Shifting X by the width of a full tile, everytime we move to a new Z slice
                    var targetX = X + Z * size;

                    // 2D to 1D Coords
                    var j = targetX + Y * width;

                    // Put the color in a new place in the 2D texture
                    tempColors[j] = colors[i];
                }

                t2d.SetPixels(tempColors);
                t2d.Apply();

                ctx.AddObjectToAsset("lut", t2d);
                ctx.SetMainObject(t2d);
            }
//            // Skip PPv2 files explicitly just in case
//            string filename = Path.GetFileNameWithoutExtension(ctx.assetPath);
//            if (s_Excluded.Contains(filename))
//                return;
//
//            bool success = ParseCubeData(ctx, out int lutSize, out Color[] pixels);
//            if (!success)
//                return;
//
//            var tex = new Texture3D(lutSize, lutSize, lutSize, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
//            {
//                filterMode = FilterMode.Bilinear,
//                wrapMode = TextureWrapMode.Clamp,
//                anisoLevel = 0
//            };
//            tex.SetPixels(pixels);
//
//            ctx.AddObjectToAsset("3D Lookup Texture", tex);
//            ctx.SetMainObject(tex);
        }

        // Trim useless spaces and remove comments
        string FilterLine(string line)
        {
            var filtered = new StringBuilder();
            line = line.TrimStart().TrimEnd();
            int len = line.Length;
            int o = 0;

            while (o < len)
            {
                char c = line[o];
                if (c == '#') break; // Comment filtering
                filtered.Append(c);
                o++;
            }

            return filtered.ToString();
        }

        bool ParseCubeData(AssetImportContext ctx, out int lutSize, out Color[] pixels)
        {
            // Quick & dirty error utility
            bool Error(string msg)
            {
                ctx.LogImportError(msg);
                return false;
            }

            var lines = File.ReadAllLines(ctx.assetPath);
            pixels = null;
            lutSize = -1;

            // Start parsing
            int sizeCube = -1;
            var table = new List<Color>();

            for (int i = 0; true; i++)
            {
                // EOF
                if (i >= lines.Length)
                {
                    if (table.Count != sizeCube)
                        return Error("Premature end of file");

                    break;
                }

                // Cleanup & comment removal
                var line = FilterLine(lines[i]);

                if (string.IsNullOrEmpty(line))
                    continue;

                // Header data
                if (line.StartsWith("TITLE"))
                    continue; // Skip the title tag, we don't need it

                if (line.StartsWith("LUT_3D_SIZE"))
                {
                    var sizeStr = line.Substring(11).TrimStart();

                    if (!int.TryParse(sizeStr, out var size))
                        return Error($"Invalid data on line {i}");

                    if (size < k_MinLutSize || size > k_MaxLutSize)
                        return Error("LUT size out of range");

                    lutSize = size;
                    sizeCube = size * size * size;

                    continue;
                }

                if (line.StartsWith("DOMAIN_"))
                    continue; // Skip domain boundaries, haven't run into a single cube file that used them

                // Table
                var row = line.Split();

                if (row.Length != 3)
                    return Error($"Invalid data on line {i}");

                var color = Color.black;

                for (int j = 0; j < 3; j++)
                {
                    if (!float.TryParse(row[j], NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out var d))
                        return Error($"Invalid data on line {i}");

                    color[j] = d;
                }

                table.Add(color);
            }

            if (sizeCube != table.Count)
                return Error($"Wrong table size - Expected {sizeCube} elements, got {table.Count}");

            pixels = table.ToArray();
            return true;
        }
    }
}
