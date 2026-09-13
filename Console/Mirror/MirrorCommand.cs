using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Media;
using DmdExt.Common;
using LibDmd;
using LibDmd.Common;
using LibDmd.Converter;
using LibDmd.Converter.Vni;
using LibDmd.DmdDevice;
using LibDmd.Frame;
using LibDmd.Input;
using LibDmd.Input.FutureDmd;
using LibDmd.Input.PinballFX;
#if !DMDEXT_MIRROR_ONLY
using LibDmd.Input.ProPinball;
#endif
using LibDmd.Input.ScreenGrabber;
using LibDmd.Input.TPAGrabber;
using LibDmd.Output;
using LibDmd.Output.FileOutput;
using LibDmd.Output.Network;
using LibDmd.Output.Virtual.Backglass;
using LibDmd.Processor;

namespace DmdExt.Mirror
{
	class MirrorCommand : BaseCommand
	{
		private readonly MirrorOptions _options;
		private readonly IConfiguration _config;
		private ColorizationLoader _colorizationLoader;
		private readonly CompositeDisposable _subscriptions = new CompositeDisposable();
		private List<IDestination> _renderers;
		private VirtualBackglass _backglass;

		public MirrorCommand(IConfiguration config, MirrorOptions options)
		{
			_config = config;
			_options = options;
			if (_options.SkipAnalytics) {
				Analytics.Instance.Disable();
			}
		}

		private RenderGraph CreateGraph(ISource source, string name, HashSet<string> reportingTags)
		{
			if (_renderers == null) {
				_renderers = GetRenderers(_config, reportingTags);
			}
			var graph = new RenderGraph(new UndisposedReferences()) {
				Name = name,
				Source = source,
				Destinations = _renderers,
				Resize = _config.Global.Resize,
				FlipHorizontally = _config.Global.FlipHorizontally,
				FlipVertically = _config.Global.FlipVertically,
				IdleAfter = _options.IdleAfter,
				IdlePlay = _options.IdlePlay
			};
			graph.SetColor(_config.Global.DmdColor);
			var palette = GetStaticPalette();
			if (palette != null) {
				graph.SetPalette(palette);
				graph.Destinations.OfType<IPaletteDestination>().ToList().ForEach(dest => dest.SetPalette(palette));
			}

			return graph;
		}

		private Color[] GetStaticPalette()
		{
			if (_options.Colors.Length == 0) {
				return null;
			}

			var colors = _options.Colors.Select(ColorUtil.ParseColor).ToArray();
			if (colors.Length == 16) {
				return colors;
			}

			var palette = new Color[16];
			for (var i = 0; i < palette.Length; i++) {
				palette[i] = i < colors.Length ? colors[i] : colors[colors.Length - 1];
			}
			return palette;
		}

		protected override void CreateRenderGraphs(RenderGraphCollection graphs, HashSet<string> reportingTags)
		{
			// setup source and additional processors
			switch (_options.Source) {

				case SourceType.PinballFX2: {
					reportingTags.Add("In:PinballFX2");
					Analytics.Instance.SetSource("Pinball FX2");
					graphs.Add(CreateGraph(new PinballFX2Grabber { FramesPerSecond = _options.FramesPerSecond }, "Pinball FX2 Render Graph", reportingTags));
					break;
				}

				case SourceType.PinballFX3: {
					if (_options.Fx3GrabScreen) {
						reportingTags.Add("In:PinballFX3Legacy");
						Analytics.Instance.SetSource("Pinball FX3 (legacy)");
						graphs.Add(CreateGraph(new PinballFX3Grabber { FramesPerSecond = _options.FramesPerSecond }, "Pinball FX3 (legacy) Render Graph", reportingTags));
					} else {
						reportingTags.Add("In:PinballFX3"); // analytics done when game name is known
						graphs.Add(CreateGraph(new PinballFX3MemoryGrabber { FramesPerSecond = _options.FramesPerSecond }, "Pinball FX3 Render Graph", reportingTags));
					}
					break;
				}

				case SourceType.PinballArcade: {
					reportingTags.Add("In:PinballArcade"); // analytics done when game name is known
					var tpaGrabber = new TPAGrabber { FramesPerSecond = _options.FramesPerSecond };
					graphs.Add(CreateGraph(tpaGrabber.Gray2Source, "Pinball Arcade (2-bit) Render Graph", reportingTags));
					graphs.Add(CreateGraph(tpaGrabber.Gray4Source, "Pinball Arcade (4-bit) Render Graph", reportingTags));
					break;
				}

#if !DMDEXT_MIRROR_ONLY
				case SourceType.ProPinball: {
					reportingTags.Add("In:ProPinball");
					Analytics.Instance.SetSource("Pro Pinball", "Timeshock");
					graphs.Add(CreateGraph(new ProPinballSlave(_options.ProPinballArgs), "Pro Pinball Render Graph", reportingTags));
					break;
				}
#endif

				case SourceType.Screen:
					var grabber = new ScreenGrabber {
						FramesPerSecond = _options.FramesPerSecond,
						Left = _options.Position[0],
						Top = _options.Position[1],
						Width = _options.Position[2] - _options.Position[0],
						Height = _options.Position[3] - _options.Position[1],
						DestinationDimensions = new Dimensions(_options.ResizeTo[0], _options.ResizeTo[1])
					};
					if (_options.GridSpacing > 0) {
						grabber.Processors.Add(new GridProcessor {
							Width = _options.ResizeTo[0],
							Height = _options.ResizeTo[1],
							Spacing = _options.GridSpacing
						});
					}

					reportingTags.Add("In:ScreenGrab");
					Analytics.Instance.SetSource("Screen Grabber");
					graphs.Add(CreateGraph(grabber, "Screen Grabber Render Graph", reportingTags));
					break;

				case SourceType.FuturePinball:
					reportingTags.Add("In:FutureDmdSink");
					Analytics.Instance.SetSource("Future Pinball");
					graphs.Add(CreateGraph(new FutureDmdSink(_options.FramesPerSecond), "Future Pinball Render Graph", reportingTags));
					break;

				default:
					throw new ArgumentOutOfRangeException();
			}

			// if colorization enabled, subscribe to name changes to re-load colorizer.
			if (_config.Global.Colorize) {
				foreach (var graph in graphs.Graphs) {
					if (!(graph.Source is IGameNameSource gameNameSource)) {
						Analytics.Instance.SetSource(graph.Source.Name);
						Analytics.Instance.StartGame(); // send now, since we won't get a game name
						continue;
					}

					if (_colorizationLoader == null) {
						_colorizationLoader = new ColorizationLoader();
						graphs.ClearColor();
					}
					
					var converter = new SwitchingConverter();
					graph.Converter = converter;

					_subscriptions.Add(gameNameSource.GetGameName().Subscribe(name => {
						converter.Switch(SetupColorizer(name));
					}));

					if (graph.Source is IDmdColorSource dmdColorSource) {
						dmdColorSource.GetDmdColor().Subscribe(color => {
							converter.SetColor(color);
						});
					}
				}
				
				// analytics
				var g = graphs.Graphs.FirstOrDefault();
				if (g != null) {
					if (g.Source is IGameNameSource s) {
						_subscriptions.Add(s.GetGameName().Where(name => name != null).Subscribe(name => {
							Analytics.Instance.SetSource(g.Source.Name, name);
							Analytics.Instance.StartGame();
						}));
						
					} else {
						Analytics.Instance.SetSource(g.Source.Name);
						Analytics.Instance.StartGame(); // send now, since we won't get a game name
					}
				}
				
			} else {

				// When not colorizing, subscribe to DMD color changes to inform the graph.
				var colorSub = graphs.Graphs
					.Select(g => g.Source as IDmdColorSource)
					.FirstOrDefault(s => s != null)
					?.GetDmdColor()
					.Subscribe(graphs.SetColor);
				if (colorSub != null) {
					_subscriptions.Add(colorSub);
				}
				
				// print game names & analytics
				var graph = graphs.Graphs.FirstOrDefault();
				if (graph != null) {
					if (graph.Source is IGameNameSource s) {
						var nameSub = s.GetGameName().Where(name => name != null).Subscribe(name => {
							Logger.Info($"New game detected at {graph.Source.Name}: {name}");
							Analytics.Instance.SetSource(graph.Source.Name, name);
							Analytics.Instance.StartGame();
						});
						_subscriptions.Add(nameSub);
					} else {
						Analytics.Instance.SetSource(graph.Source.Name);
						Analytics.Instance.StartGame(); // send now, since we won't get a game name
					}
				}
			}

			// stream the game name, which the source only knows once the game is running
			if (_config.NetworkStream.Enabled) {
				var graph = graphs.Graphs.FirstOrDefault();
				if (graph?.Source is IGameNameSource s) {
					var networkStream = graph.Destinations.OfType<NetworkStream>().FirstOrDefault();
					if (networkStream != null) {
						_subscriptions.Add(s.GetGameName().Subscribe(name => networkStream.SetGameName(name ?? "")));
					}
				}
			}

			// while a source reports no game, idle: play --idle-play or clear the display
			foreach (var graph in graphs.Graphs) {
				if (graph.Source is IGameNameSource gameNameSource) {
					_subscriptions.Add(gameNameSource.GetGameName().Subscribe(name => {
						if (name == null) {
							graph.StartIdling();
						} else {
							graph.StopIdling();
						}
					}));
				}
			}

			// backglass window, showing the image named after the running game
			if (_options.Backglass) {
				var graph = graphs.Graphs.FirstOrDefault(g => g.Source is IGameNameSource);
				if (graph != null) {
					var position = _options.BackglassPosition;
					_backglass = new VirtualBackglass(position[0], position[1], position[2], position[3]);
					_backglass.SetImage(_options.BackglassIdle);
					_subscriptions.Add(((IGameNameSource)graph.Source).GetGameName().Subscribe(name => {
						_backglass.SetImage(FindBackglassImage(graph.Source, name));
					}));
				} else {
					Logger.Warn("No backglass window, since the source doesn't report which game is running.");
				}
			}

			// raw output
			if (_config.RawOutput.Enabled) {
				var graph = graphs.Graphs.FirstOrDefault();
				if (graph?.Source is IGameNameSource s) {
					var rawOutput = graph.Destinations.OfType<RawOutput>().FirstOrDefault();
					if (rawOutput != null) {
						var nameSub = s.GetGameName().Where(name => name != null).Subscribe(name => {
							rawOutput.SetGameName(name);
						});
						_subscriptions.Add(nameSub);

					} else {
						Logger.Warn("Cannot enable raw output due to missing RawOutput destination.");
					}
				}
			}
		}

		public override void Dispose()
		{
			base.Dispose();
			_subscriptions.Dispose();
			_backglass?.Dispose();
		}

		/// <summary>
		/// Name of an image in the backglass folder shown while no game is running, before the
		/// default image of the source's game.
		/// </summary>
		private const string DefaultIdleImageName = "DEFAULT_IDLE";

		/// <summary>
		/// Returns the path of the backglass image of the given game. If no game is running or
		/// the game has no image, returns --backglass-idle, or else DEFAULT_IDLE.png (or .jpg)
		/// in the backglass folder, or else the default image of the source's game, or null if
		/// there's none.
		/// </summary>
		private string FindBackglassImage(ISource source, string gameName)
		{
			var folder = _options.BackglassPath;
			string defaultName = null;
			if (source is PinballFX3MemoryGrabber fx3) {
				// Pinball FX3 / Classic: next to the table files
				if (folder == null && fx3.GameFolder != null) {
					folder = Path.Combine(fx3.GameFolder, "data", "steam");
				}
				defaultName = "PinballFX3";
			}

			if (gameName != null) {
				var image = folder == null ? null : FindImage(folder, gameName);
				if (image != null) {
					Logger.Info("Showing backglass {0}.", image);
					return image;
				}
				if (folder == null) {
					Logger.Warn("No backglass folder known for {0}, set one with --backglass-path.", gameName);
				} else {
					Logger.Warn("No backglass image for {0} in {1}.", gameName, folder);
				}
			}

			if (_options.BackglassIdle != null) {
				return _options.BackglassIdle;
			}
			if (folder == null) {
				return null;
			}

			// an own idle image in the backglass folder, else the default image of the source's game
			foreach (var name in new[] { DefaultIdleImageName, defaultName }) {
				var image = name == null ? null : FindImage(folder, name);
				if (image != null) {
					Logger.Info("Showing default backglass {0}.", image);
					return image;
				}
			}
			return null;
		}

		/// <summary>
		/// Returns the path of the PNG or JPG image with the given name in the given folder, or
		/// null if there's none.
		/// </summary>
		private static string FindImage(string folder, string name)
		{
			foreach (var extension in new[] { ".png", ".jpg" }) {
				var path = Path.Combine(folder, name + extension);
				if (File.Exists(path)) {
					return path;
				}
			}
			return null;
		}

		private AbstractConverter SetupColorizer(string gameName)
		{
			// only setup if enabled and path is set
			if (!_config.Global.Colorize || _colorizationLoader == null || gameName == null) {
				Analytics.Instance.ClearColorizer();
				return null;
			}

			// 1. check for serum
			var serumColorizer = _colorizationLoader.LoadSerum(gameName, _config.Global.ScalerMode);
			if (serumColorizer != null) {
				return serumColorizer;
			}

			// 2. check for plugins
			var pluginColorizer = _colorizationLoader.LoadPlugin(_config.Global.Plugins, true, gameName, Colors.OrangeRed, null);
			if (pluginColorizer != null) {
				return pluginColorizer;
			}

			// 3. check for native pin2color
			return _colorizationLoader.LoadVniColorizer(gameName, _config.Global.ScalerMode, _config.Global.VniKey);
		}
	}
}
