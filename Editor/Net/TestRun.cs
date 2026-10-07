using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class TestRun
{
	public string Id { get; set; }
	public string ProjectId { get; set; }
	public string DeveloperId { get; set; }
	public string AgentId { get; set; }
	public string CommitSha { get; set; }
	public string Branch { get; set; }
	public string Build { get; set; }
	public string Scene { get; set; }
	public string Description { get; set; }
	public string Status { get; set; }
	public List<string> Errors { get; set; } = new();
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? FinishedAt { get; set; }
}
