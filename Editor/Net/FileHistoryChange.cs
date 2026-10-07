using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class FileHistoryChange
{
	public string Id { get; set; }
	public string Summary { get; set; }
	public string DeveloperName { get; set; }
	public DateTimeOffset? CompletedAt { get; set; }
	public bool Breaking { get; set; }
	public long? TaskId { get; set; }
}
