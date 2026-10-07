using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class FileHistoryCommit
{
	public string Sha { get; set; }
	public string ShortSha { get; set; }
	public string Message { get; set; }
	public string Author { get; set; }
	public string DeveloperId { get; set; }
	public DateTimeOffset? At { get; set; }
	public string Branch { get; set; }
	public long? TaskId { get; set; }
	public string Url { get; set; }
	public string Change { get; set; }

	public string Headline => (Message ?? "").Split( '\n' )[0];
	public string Short => !string.IsNullOrEmpty( ShortSha ) ? ShortSha : Sha?.Length > 7 ? Sha[..7] : Sha;
}
