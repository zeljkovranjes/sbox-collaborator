using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class Blocker
{
	public string Kind { get; set; }
	public string Title { get; set; }
	public string Detail { get; set; }
	public DateTimeOffset? At { get; set; }
	public string RefId { get; set; }
}
