using LibDmd.Output;

namespace LibDmd
{
	/// <summary>
	/// No-op analytics for the .NET 8 mirror-only build (LibDmd.Net8.csproj), which
	/// ships without RudderStack and WMI. Same public surface as Analytics.cs.
	/// </summary>
	public class Analytics
	{
		private static Analytics _instance;

		public static Analytics Instance => _instance ?? (_instance = new Analytics());

		public void Disable(bool log = true) { }

		public void Init(string version, string runner) { }

		public void StartGame() { }

		public void SetSource(string source, string gameId) { }

		public void SetSource(string host) { }

		public void ClearSource() { }

		public void AddDestination(IDestination dest) { }

		public void ClearVirtualDestinations() { }

		public void SetColorizer(string name) { }

		public void ClearColorizer() { }

		public void EndGame() { }
	}
}
