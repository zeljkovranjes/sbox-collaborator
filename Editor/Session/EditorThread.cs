using System.Runtime.CompilerServices;

namespace Collaborator.EditorTools.Session;

/// <summary>
/// Hop onto the editor's main thread: <c>await EditorThread.SwitchToMainThread();</c>.
/// Widgets, the asset system and session state must only be touched there.
/// </summary>
public static class EditorThread
{
	public static MainThreadAwaitable SwitchToMainThread() => default;

	public readonly struct MainThreadAwaitable : INotifyCompletion
	{
		public MainThreadAwaitable GetAwaiter() => this;
		public bool IsCompleted => ThreadSafe.IsMainThread;
		public void OnCompleted( Action continuation ) => MainThread.Queue( continuation );
		public void GetResult() { }
	}

	/// <summary>Runs <paramref name="action"/> on the main thread (now when already there).</summary>
	public static void Post( Action action )
	{
		if ( ThreadSafe.IsMainThread )
			action();
		else
			MainThread.Queue( action );
	}
}
