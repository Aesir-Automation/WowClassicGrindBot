namespace WowBot.Platform;

public static class AddonPixelDecoder
{
    // DataToColor's normal-mode frame sentinels, matching IAddonDataProvider.
    public const int FirstRgb = 0x000000;
    public const int LastRgb = 0x1E8481;

    // No output is changed unless the entire layout and both sentinels validate.
    // Config-mode calibration is a different protocol and is intentionally rejected.
    public static bool TryDecode(PixelFrame capture, ReadOnlySpan<PixelCell> cells, Span<int> output)
    {
        if (cells.Length < 2 || output.Length != cells.Length)
        {
            return false;
        }

        HashSet<int> indices = [];
        foreach (PixelCell cell in cells)
        {
            if ((uint)cell.Index >= output.Length || !indices.Add(cell.Index) ||
                (uint)cell.X >= capture.Width || (uint)cell.Y >= capture.Height)
            {
                return false;
            }
        }

        PixelCell first = cells[0];
        PixelCell last = cells[^1];
        if (first.Index != 0 || last.Index != cells.Length - 1 ||
            capture.GetRgb(first.X, first.Y) != FirstRgb || capture.GetAlpha(first.X, first.Y) != 255 ||
            capture.GetRgb(last.X, last.Y) != LastRgb || capture.GetAlpha(last.X, last.Y) != 255)
        {
            return false;
        }

        foreach (PixelCell cell in cells)
        {
            output[cell.Index] = capture.GetRgb(cell.X, cell.Y);
        }
        return true;
    }
}
