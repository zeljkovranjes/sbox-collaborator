using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class CatchUpCounts
{
	public int Commits { get; set; }
	public int Changes { get; set; }
	public int Breaking { get; set; }
	public int TasksCompleted { get; set; }
	public int TasksClaimed { get; set; }
	public int Decisions { get; set; }
	public int Knowledge { get; set; }
	public int Messages { get; set; }
	public int FailedTests { get; set; }

	public int Total => Commits + Changes + TasksCompleted + TasksClaimed + Decisions + Knowledge + Messages + FailedTests;
}
