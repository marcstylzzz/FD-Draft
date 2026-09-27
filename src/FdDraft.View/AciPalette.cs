namespace FdDraft.View
{
    /// <summary>
    /// AutoCAD's 256 index colours. ACadSharp's table has five wrong entries (29, 91, 145, 192,
    /// 203 - found against ezdxf's DXF palette); 145 drew the GRAD logo's letters bright blue
    /// where AutoCAD and MSCAD show slate (63,111,127). Everything reads index colours from here.
    /// </summary>
    public static class AciPalette
    {
        public static (byte R, byte G, byte B) Of(int index)
        {
            switch (index)
            {
                case 29: return (38, 23, 19);
                case 91: return (127, 255, 127);
                case 145: return (63, 111, 127);
                case 192: return (82, 0, 165);
                case 203: return (145, 82, 165);
            }
            if (index < 1 || index > 255) return (255, 255, 255);
            var v = ACadSharp.Color.GetIndexRGB((byte)index);
            return (v[0], v[1], v[2]);
        }

        public static uint Rgb(int index) { var (r, g, b) = Of(index); return (uint)(r << 16 | g << 8 | b); }
    }
}
