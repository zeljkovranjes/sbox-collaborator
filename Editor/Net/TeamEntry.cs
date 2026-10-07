using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class TeamEntry
{
	public Developer Developer { get; set; }
	public List<Agent> Agents { get; set; } = new();
}
