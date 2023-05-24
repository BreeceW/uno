﻿#nullable enable

using System;
using Uno.Media.Playback;
using Windows.Media.Core;
using Uno.Extensions;
using System.IO;
using Uno.Foundation.Logging;
using System.Collections.Generic;
using Uno;
using Uno.Helpers;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;
using System.Diagnostics.CodeAnalysis;
using Windows.ApplicationModel.Background;
using Uno.Foundation.Extensibility;
using Windows.UI.Xaml.Controls.Maps;
using System.Numerics;
using Uno.Logging;
using Windows.UI.Xaml;
using Atk;

[assembly: ApiExtension(typeof(IMediaPlayerExtension), typeof(Uno.UI.Media.MediaPlayerExtension))]

namespace Uno.UI.Media;

public partial class MediaPlayerExtension : IMediaPlayerExtension
{
	private static Dictionary<Windows.Media.Playback.MediaPlayer, MediaPlayerExtension> _instances = new();

	private Uri? _uri;
	private List<Uri>? _playlistItems;
	private readonly Windows.Media.Playback.MediaPlayer _owner;
	private GtkMediaPlayer? _player;
	private bool _updatingPosition;
	private bool _isPlayRequested;
	private bool _isPlayerPrepared;
	private int _playlistIndex;
	private TimeSpan _naturalDuration;
	private bool _isLoopingEnabled;

	public MediaPlayerExtension(object owner)
	{
		if (owner is Windows.Media.Playback.MediaPlayer player)
		{
			_owner = player;
		}
		else
		{
			throw new InvalidOperationException($"MediaPlayerPresenterExtension must be initialized with a MediaPlayer instance");
		}

		lock (_instances)
		{
			_instances[_owner] = this;
		}
	}

	~MediaPlayerExtension()
	{
		lock (_instances)
		{
			_instances.Remove(_owner);
		}
	}

	internal static MediaPlayerExtension? GetByMediaPlayer(Windows.Media.Playback.MediaPlayer mediaPlayer)
	{
		lock (_instances)
		{
			return _instances.TryGetValue(mediaPlayer, out var instance) ? instance : null;
		}
	}

	internal GtkMediaPlayer? GtkMediaPlayer
	{
		get => _player;
		set
		{
			if (value != null)
			{
				_player = value;
				InitializePlayer();
			}
		}
	}

	public IMediaPlayerEventsExtension? Events { get; set; }

	private void InitializePlayer()
	{
		if (_player is null)
		{
			return;
		}

		_player.OnSourceFailed -= OnError;
		_player.OnSourceLoaded -= OnPrepared;
		_player.OnSourceEnded -= OnCompletion;
		_player.OnTimeUpdate -= OnTimeUpdate;
		_player.OnSourceFailed += OnError;
		_player.OnSourceLoaded += OnPrepared;
		_player.OnSourceEnded += OnCompletion;
		_player.OnTimeUpdate += OnTimeUpdate;

		_owner.PlaybackSession.PlaybackStateChanged -= OnStatusChanged;
		_owner.PlaybackSession.PlaybackStateChanged += OnStatusChanged;

		ApplyVideoSource();
	}

	public TimeSpan Position
	{
		get => TimeSpan.FromSeconds(_player?.CurrentPosition ?? 0);
		set
		{
			if (!_updatingPosition)
			{
				_updatingPosition = true;

				try
				{
					if (_owner.PlaybackSession.PlaybackState != MediaPlaybackState.None && _player is not null && _player.Source is not null)
					{
						_player.CurrentPosition = (int)value.TotalMilliseconds;
						OnSeekComplete();
					}
				}
				finally
				{
					_updatingPosition = false;
				}
			}
		}
	}

	private void ApplyVideoSource()
	{
		if (_player is not null && _uri is not null)
		{
			_player.Source = _uri.OriginalString;
		}
	}

	public void Pause()
	{
		if (_owner.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
		{
			_player?.Pause();
			_owner.PlaybackSession.PlaybackState = MediaPlaybackState.Paused;
		}
	}

	public void InitializeSource()
	{
		NaturalDuration = TimeSpan.Zero;
		if (Position != TimeSpan.Zero)
		{
			Position = TimeSpan.Zero;
		}

		if (_owner.Source == null)
		{
			return;
		}
		_owner.PlaybackSession.PlaybackState = MediaPlaybackState.Opening;
		InitializePlayer();

		switch (_owner.Source)
		{
			case MediaPlaybackList playlist when playlist.Items.Count > 0 && _playlistItems is not null:
				SetPlaylistItems(playlist);
				_uri = _playlistItems[0];
				break;

			case MediaPlaybackItem item:
				_uri = item.Source.Uri;
				break;

			case MediaSource source:
				_uri = source.Uri;
				break;

			default:
				throw new InvalidOperationException("Unsupported media source type");
		}
		ApplyVideoSource();
		Events?.RaiseMediaOpened();
		Events?.RaiseSourceChanged();

	}

	private void SetPlaylistItems(MediaPlaybackList playlist)
	{
		_playlistItems = playlist.Items
			.Select(i => i.Source.Uri)
			.ToList();
	}

	public void Stop()
	{
		if (_owner.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
		{
			_player?.Stop();
			_owner.PlaybackSession.PlaybackState = MediaPlaybackState.Paused;
		}
	}

	public void Play()
	{
		if (_owner.Source == null || _player == null)
		{
			return;
		}

		try
		{
			// If we reached the end of media, we need to reset position to 0
			if (_owner.PlaybackSession.PlaybackState == MediaPlaybackState.None)
			{
				_owner.PlaybackSession.Position = TimeSpan.Zero;
			}

			_isPlayRequested = true;

			if (_isPlayerPrepared)
			{
				if (_player != null)
				{
					_player.PlaybackRate = 1;
					_player.Play();
				}
				_owner.PlaybackSession.PlaybackState = MediaPlaybackState.Playing;
			}
			else
			{
				if (this.Log().IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
				{
					this.Log().Debug("The player is not prepared yet, delaying playback");
				}
			}
		}
		catch (global::System.Exception ex)
		{
			OnMediaFailed(ex);
		}
	}

	public void Initialize()
		=> InitializePlayer();

	private double _playbackRate;

	public double PlaybackRate
	{
		get => _playbackRate;
		set
		{
			_playbackRate = value;
			if (_player is not null)
			{
				_player.PlaybackRate = value;
			}
		}
	}

	public bool IsLoopingEnabled
	{
		get => _isLoopingEnabled;
		set
		{
			_isLoopingEnabled = value;
			if (_player is not null)
			{
				_player.SetIsLoopingEnabled(value);
			}
		}
	}

	public MediaPlayerState CurrentState
	{
		get => _player == null ? default(MediaPlayerState) : _player.CurrentState;
		internal set
		{
			if (_player != null)
			{
				_player.CurrentState = value;
			}
		}
	}

	public TimeSpan NaturalDuration
	{
		get => _naturalDuration;
		internal set
		{
			_naturalDuration = value;

			Events?.NaturalDurationChanged();
		}
	}

	public bool IsProtected
			=> false;

	public double BufferingProgress
		=> 0.0;

	public bool CanPause
		=> true;

	public bool CanSeek
		=> true;

	public MediaPlayerAudioDeviceType AudioDeviceType { get; set; }

	public MediaPlayerAudioCategory AudioCategory { get; set; }

	public TimeSpan TimelineControllerPositionOffset
	{
		get => Position;
		set => Position = value;
	}

	public bool RealTimePlayback { get; set; }

	public double AudioBalance { get; set; }

	public void SetTransportControlsBounds(Rect bounds)
	{
		_player?.SetTransportControlsBounds(bounds);
	}

	public void SetUriSource(Uri uri)
	{
		if (this.Log().IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
		{
			this.Log().Debug($"MediaPlayerExtension.SetUriSource({uri})");
		}
		if (_player is not null)
		{
			_player.Source = uri.OriginalString;
		}
	}

	public void SetFileSource(IStorageFile file) => throw new NotImplementedException();

	public void SetStreamSource(IRandomAccessStream stream) => throw new NotImplementedException();

	public void SetMediaSource(IMediaSource source) => throw new NotImplementedException();

	public void StepForwardOneFrame() => throw new NotImplementedException();

	public void StepBackwardOneFrame() => throw new NotImplementedException();

	public void SetSurfaceSize(Size size) => throw new NotImplementedException();

	public void ToggleMute()
	{
		if (_player is not null)
		{
			_player?.Mute(_owner.IsMuted);
		}
	}

	public void OnOptionChanged(string name, object value) => throw new NotImplementedException();

	public void Dispose()
	{
		_instances.Remove(_owner);

		TryDisposePlayer();
	}

	private void TryDisposePlayer()
	{
		if (_player != null)
		{
			_isPlayRequested = false;
			_isPlayerPrepared = false;
		}
	}
}
