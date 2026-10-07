using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class TaskNote
{
	public string Id { get; set; }
	public long TaskId { get; set; }
	public string Kind { get; set; }
	public string AuthorId { get; set; }
	public string AuthorName { get; set; }
	public string Summary { get; set; }
	public string Next { get; set; }
	public string Gotchas { get; set; }
	public List<string> Files { get; set; } = new();
	public DateTimeOffset? CreatedAt { get; set; }
}
