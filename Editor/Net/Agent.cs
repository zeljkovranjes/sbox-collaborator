using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class Agent
{
	public string Id { get; set; }
	public string DeveloperId { get; set; }
	public string DeveloperName { get; set; }
	public string ProjectId { get; set; }
	public string ClientType { get; set; }
	public string Machine { get; set; }
	public string Model { get; set; }
	public string Label { get; set; }
	public string Status { get; set; }
	public string StatusNote { get; set; }
	public long? CurrentTaskId { get; set; }
	public string CurrentTaskTitle { get; set; }
	public string Branch { get; set; }
	public List<string> Files { get; set; } = new();
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? LastHeartbeatAt { get; set; }
	public bool Online { get; set; }

	public string DisplayLabel => !string.IsNullOrEmpty( Label ) ? Label : ClientType ?? "agent";
}
