using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class Change
{
	public string Id { get; set; }
	public string Status { get; set; }
	public string Summary { get; set; }
	public string DeveloperId { get; set; }
	public string DeveloperName { get; set; }
	public long? TaskId { get; set; }
	public string CommitSha { get; set; }
	public List<string> BreakingChanges { get; set; } = new();
	public DateTimeOffset? CompletedAt { get; set; }
}
