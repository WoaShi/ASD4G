namespace ASD4G.Models;

public sealed class DisplayMode
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int BitsPerPixel { get; init; }

    public required int DisplayFrequency { get; init; }
}
