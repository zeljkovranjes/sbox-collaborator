using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class TeamMessage
{
	public long Id { get; set; }
	public string ProjectId { get; set; }
	public string Type { get; set; }
	public string FromDeveloperId { get; set; }
	public string FromName { get; set; }
	public string FromAgentId { get; set; }
	public string ToDeveloperId { get; set; }
	public string ToAgentId { get; set; }
	public bool Broadcast { get; set; }
	public string Subject { get; set; }
	public string Body { get; set; }
	public long? TaskId { get; set; }
	public List<string> Paths { get; set; } = new();
	public DateTimeOffset? CreatedAt { get; set; }
	public DateTimeOffset? ReadAt { get; set; }
	public DateTimeOffset? AckedAt { get; set; }
}
