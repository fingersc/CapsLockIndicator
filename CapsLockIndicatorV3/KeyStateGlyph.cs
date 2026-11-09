/*
 * Copyright (c) Jonas Kohl <https://jonaskohl.de/>
 */
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CapsLockIndicatorV3
{
    /// <summary>
    /// Draws the key specific indicator glyphs that are shown next to the text
    /// of the overlay:
    ///
    /// Caps lock   -> "A"
    /// Num lock    -> "5"
    /// Scroll lock -> down arrow
    ///
    /// If the key is not active, the glyph is crossed out with a diagonal line
    /// (from top left to bottom right, at a shallower angle than 45 degrees so
    /// that it can not be confused with the leg of the letter "A").
    ///
    /// The letters are taken from the outlines of a system font and the arrow
    /// is built from simple shapes. All glyphs are filled as vector paths: they
    /// are scaled to exactly the same height, so they all have the same optical
    /// size, and they stay sharp on any display scale. The additional drawing
    /// operations are negligible compared to rendering the text itself.
    /// </summary>
    internal static class KeyStateGlyph
    {
        /// <summary>
        /// The height of every glyph, relative to the height of the box it is
        /// drawn in. The same value is used for the letters and for the arrow,
        /// so that all three glyphs appear equally large.
        /// </summary>
        private const float GlyphHeightRatio = 0.84f;

        /// <summary>
        /// The font families that are used to build the letter outlines, in the
        /// order they are tried.
        /// </summary>
        private static readonly string[] letterFontCandidates =
        {
            "Segoe UI",
            "Tahoma",
            "Arial",
            "Microsoft Sans Serif"
        };

        private static FontFamily letterFontFamily;
        private static bool letterFontFamilyResolved;

        /// <summary>
        /// Returns true if there is a dedicated glyph for the given key.
        /// </summary>
        public static bool IsSupported(IndicatorKey key)
        {
            return key == IndicatorKey.Caps || key == IndicatorKey.Num || key == IndicatorKey.Scroll;
        }

        /// <summary>
        /// Returns the edge length of the (square) area the glyph is drawn in.
        /// The glyph is slightly bigger than the text so that it stays clearly
        /// visible, even with the diagonal line through it.
        /// </summary>
        public static int GetBoxSize(Font font)
        {
            if (font == null)
                return 24;

            return Math.Max(18, (int)Math.Round(font.Height * 1.1));
        }

        /// <summary>
        /// Draws the glyph for the given key into the given area. If the key is
        /// not active, a diagonal line is drawn through the glyph.
        /// </summary>
        public static void Draw(Graphics g, IndicatorKey key, bool isActive, Rectangle box, Color colour)
        {
            if (g == null || !IsSupported(key))
                return;

            if (box.Width <= 0 || box.Height <= 0)
                return;

            // The glyphs are filled shapes, so they need anti aliasing to look
            // as smooth as the text next to them
            var previousSmoothingMode = g.SmoothingMode;
            var previousPixelOffsetMode = g.PixelOffsetMode;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            try
            {
                switch (key)
                {
                    case IndicatorKey.Caps:
                        DrawLetter(g, box, colour, "A");
                        break;
                    case IndicatorKey.Num:
                        DrawLetter(g, box, colour, "5");
                        break;
                    case IndicatorKey.Scroll:
                        DrawArrow(g, box, colour);
                        break;
                    default:
                        break;
                }

                if (!isActive)
                    DrawSlash(g, box, colour);
            }
            finally
            {
                g.SmoothingMode = previousSmoothingMode;
                g.PixelOffsetMode = previousPixelOffsetMode;
            }
        }

        /// <summary>
        /// Draws a letter (or digit) by taking its outline from a system font
        /// and scaling it up so that its ink is exactly as high as the other
        /// glyphs. Using the font outline instead of drawing the letter by hand
        /// gives it the proportions people expect from a letter.
        /// </summary>
        private static void DrawLetter(Graphics g, Rectangle box, Color colour, string text)
        {
            var family = GetLetterFontFamily();

            if (family == null)
                return;

            using (var path = new GraphicsPath())
            {
                // Add the glyph outline at an arbitrary but big em size; the
                // path is scaled to the target size below
                path.AddString(
                    text,
                    family,
                    (int)FontStyle.Bold,
                    100f,
                    new PointF(0f, 0f),
                    StringFormat.GenericTypographic);

                var ink = path.GetBounds();

                if (ink.Width <= 0f || ink.Height <= 0f)
                    return;

                // Scale the ink to the shared glyph height and center it in the
                // box (both horizontally and vertically)
                float scale = box.Height * GlyphHeightRatio / ink.Height;
                float offsetX = box.Left + (box.Width - ink.Width * scale) / 2f;
                float offsetY = box.Top + (box.Height - ink.Height * scale) / 2f;

                using (var matrix = new Matrix(
                    scale, 0f,
                    0f, scale,
                    offsetX - ink.Left * scale,
                    offsetY - ink.Top * scale))
                {
                    path.Transform(matrix);
                }

                using (var brush = new SolidBrush(colour))
                    g.FillPath(brush, path);
            }
        }

        /// <summary>
        /// Draws a filled down arrow. Its height matches the height of the
        /// letters, its vertical bar is as thick as the stems of the letters.
        /// </summary>
        private static void DrawArrow(Graphics g, Rectangle box, Color colour)
        {
            // A filled arrow carries less ink than a bold letter, so it has to
            // be drawn a little taller than the letters to look equally large
            const float arrowHeightRatio = 0.94f;

            float top = (1f - arrowHeightRatio) / 2f;
            float bottom = top + arrowHeightRatio;

            // The triangle takes the lower half of the glyph, the bar the upper
            // half (with a small overlap so both parts connect)
            float headTop = top + arrowHeightRatio * 0.44f;
            float barLeft = 0.5f - 0.10f;
            float barRight = 0.5f + 0.10f;

            using (var brush = new SolidBrush(colour))
            using (var path = new GraphicsPath())
            {
                path.FillMode = FillMode.Winding;

                path.AddPolygon(new PointF[]
                {
                    MapPoint(box, 0.12f, headTop),
                    MapPoint(box, 0.88f, headTop),
                    MapPoint(box, 0.50f, bottom)
                });

                path.AddRectangle(MapRectangle(box, barLeft, top, barRight - barLeft, headTop - top + 0.02f));

                g.FillPath(brush, path);
            }
        }

        /// <summary>
        /// Draws the diagonal line that marks an inactive key. The line runs
        /// from the top left to the bottom right at a shallow angle, so that it
        /// can not be mistaken for the leg of the letter "A".
        /// </summary>
        private static void DrawSlash(Graphics g, Rectangle box, Color colour)
        {
            using (var pen = new Pen(colour, Math.Max(2f, box.Width * 0.1f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                g.DrawLine(pen, MapPoint(box, 0.02f, 0.20f), MapPoint(box, 0.98f, 0.80f));
            }
        }

        /// <summary>
        /// Returns the font family used for the letter outlines, or null if
        /// none of the candidate families is installed.
        /// </summary>
        private static FontFamily GetLetterFontFamily()
        {
            if (letterFontFamilyResolved)
                return letterFontFamily;

            letterFontFamilyResolved = true;

            foreach (var name in letterFontCandidates)
            {
                try
                {
                    var family = new FontFamily(name);

                    if (!family.IsStyleAvailable(FontStyle.Bold))
                        continue;

                    letterFontFamily = family;
                    break;
                }
                catch (ArgumentException)
                {
                    // The family is not installed, try the next one
                }
            }

            return letterFontFamily;
        }

        /// <summary>
        /// Maps normalized coordinates (0..1) to a point inside the given area.
        /// </summary>
        private static PointF MapPoint(Rectangle box, float x, float y)
        {
            return new PointF(box.Left + x * box.Width, box.Top + y * box.Height);
        }

        /// <summary>
        /// Maps a normalized rectangle (0..1) to a rectangle inside the given
        /// area.
        /// </summary>
        private static RectangleF MapRectangle(Rectangle box, float x, float y, float width, float height)
        {
            return new RectangleF(
                box.Left + x * box.Width,
                box.Top + y * box.Height,
                width * box.Width,
                height * box.Height);
        }
    }
}
