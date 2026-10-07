using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class Reservation
{
	public string Id { get; set; }
	public string ProjectId { get; set; }
	public string Path { get; set; }
	public bool IsDirectory { get; set; }
	public string DeveloperId { get; set; }
	public string DeveloperName { get; set; }
	public string AgentId { get; set; }
	public string AgentLabel { get; set; }
	public long? TaskId { get; set; }
	public string TaskTitle { get; set; }
	public string Reason { get; set; }
	public string Branch { get; set; }
	public DateTimeOffset? CreatedAt { get; set; }
	public DateTimeOffset? ExpiresAt { get; set; }
}
