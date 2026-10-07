using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class FileHistoryTask
{
	public long Id { get; set; }
	public string Title { get; set; }
	public string Status { get; set; }
	public string OwnerName { get; set; }
}
