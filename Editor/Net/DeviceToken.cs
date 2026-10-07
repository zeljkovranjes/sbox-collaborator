using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class DeviceToken
{
	public string Token { get; set; }
	public Developer Developer { get; set; }
	public AccessKey Key { get; set; }
}
