using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class CollabProject
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Kind { get; set; }
	public string PackageIdent { get; set; }
	public string DefaultBranch { get; set; }
	public string Summary { get; set; }
	public string Milestone { get; set; }
	public string Conventions { get; set; }

	public string Title => string.IsNullOrEmpty( Name ) ? Id : Name;
}
