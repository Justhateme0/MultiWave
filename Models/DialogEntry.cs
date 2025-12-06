using TL;

namespace MultiWave.Models;

public class DialogEntry
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public InputPeer Peer { get; init; } = null!;
}
