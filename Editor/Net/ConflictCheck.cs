using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class ConflictCheck
{
	public bool Clear { get; set; }
	public List<Conflict> Conflicts { get; set; } = new();
	public string Message { get; set; }
}
