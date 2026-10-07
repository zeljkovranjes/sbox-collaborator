using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class BulkAssetResult
{
	public int Upserted { get; set; }
	public int Links { get; set; }
	public int Removed { get; set; }
}
