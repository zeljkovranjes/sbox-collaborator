using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class HeartbeatResult
{
	public Agent Agent { get; set; }
	public int UnreadMessages { get; set; }
	public List<string> Notices { get; set; } = new();
}
