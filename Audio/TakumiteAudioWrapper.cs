using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace TakumiteAudioWrapper
{
    /// <summary>
    /// Windows Audio Session管理クラス
    /// </summary>
    public static class WindowsAudioManager
    {
        private static MMDeviceEnumerator deviceEnumerator;
        private static MMDevice defaultDevice;
        private static AudioSessionManager sessionManager;

        static WindowsAudioManager()
        {
            try
            {
                deviceEnumerator = new MMDeviceEnumerator();
                defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                sessionManager = defaultDevice.AudioSessionManager;
                System.Diagnostics.Debug.WriteLine("✅ Windows Audio Session Manager初期化完了");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Windows Audio Session Manager初期化失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のプロセスの音量を設定（プロセス固有）
        /// </summary>
        public static void SetApplicationVolume(float volume)
        {
            try
            {
                if (sessionManager == null) return;

                var sessions = sessionManager.Sessions;
                int currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
                
                // 現在のプロセスIDに一致するセッションのみに音量を適用
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    
                    try
                    {
                        // プロセスIDが一致するセッションかチェック
                        if (session.GetProcessID == currentProcessId)
                        {
                            var volumeControl = session.SimpleAudioVolume;
                            if (volumeControl != null)
                            {
                                volumeControl.Volume = Math.Max(0.0f, Math.Min(1.0f, volume));
                                System.Diagnostics.Debug.WriteLine($"🔊 アプリケーション音量設定 (PID:{currentProcessId}): {(int)(volume * 100)}%");
                                return; // 成功したら終了
                            }
                        }
                    }
                    catch
                    {
                        // このセッションは対象外、次へ
                        continue;
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"⚠️ プロセス(PID:{currentProcessId})の音量セッションが見つかりませんでした");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ アプリケーション音量設定エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のプロセスの音量を取得（プロセス固有）
        /// </summary>
        public static float GetApplicationVolume()
        {
            try
            {
                if (sessionManager == null) return 1.0f;

                var sessions = sessionManager.Sessions;
                int currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    
                    try
                    {
                        // プロセスIDが一致するセッションかチェック
                        if (session.GetProcessID == currentProcessId)
                        {
                            var volumeControl = session.SimpleAudioVolume;
                            return volumeControl?.Volume ?? 1.0f;
                        }
                    }
                    catch
                    {
                        // このセッションは対象外、次へ
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ アプリケーション音量取得エラー: {ex.Message}");
            }
            return 1.0f;
        }
    }

    /// <summary>
    /// オーディオマネージャークラス
    /// </summary>
    public class AudioManager
    {
        public AudioWrapper AddAudio(string filePath, float volume, AudioType audioType = AudioType.Effect)
        {
            return new AudioWrapper(filePath, volume, audioType);
        }
    }

    /// <summary>
    /// 音声の種別
    /// </summary>
    public enum AudioType
    {
        MainLoop,    // メインのループ音声（防護無線、列車番号設定）
        Effect,      // 効果音（beep等）
        System       // システム音（完了音等）
    }

    /// <summary>
    /// オーディオラッパークラス
    /// </summary>
    public class AudioWrapper
    {
        // Windows API for audio playback (allows multiple simultaneous sounds)
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        // PlaySound flags
        private const uint SND_FILENAME = 0x00020000;
        private const uint SND_ASYNC = 0x00000001;
        private const uint SND_NODEFAULT = 0x00000002;

        private string _filePath;
        private float _volume;
        private string _fullPath;
        private bool _isLooping;
        private System.Threading.Timer _loopTimer;
        private List<System.Media.SoundPlayer> _activePlayers;
        private readonly object _playersLock = new object();
        private AudioType _audioType;

        public AudioWrapper(string filePath, float volume, AudioType audioType = AudioType.Effect)
        {
            _filePath = filePath;
            _volume = volume;
            _audioType = audioType;
            _isLooping = false;
            _activePlayers = new List<System.Media.SoundPlayer>();
            
            try
            {
                // 実行ファイルのディレクトリを基準にしたパスを作成
                var exeDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                _fullPath = System.IO.Path.Combine(exeDirectory, _filePath);
                
                System.Diagnostics.Debug.WriteLine($"Attempting to load audio file: {_fullPath}");
                
                if (System.IO.File.Exists(_fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Audio file found: {_fullPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Audio file not found: {_fullPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// WAVファイルの長さを取得（ミリ秒）
        /// </summary>
        public int GetDurationMs()
        {
            try
            {
                if (!System.IO.File.Exists(_fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Audio file not found for duration: {_fullPath}");
                    return 5000; // デフォルト5秒
                }

                // WAVファイルのヘッダーを読み取って正確な長さを計算
                using (var fs = new FileStream(_fullPath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    // WAVヘッダーの検証
                    var riff = new string(br.ReadChars(4)); // "RIFF"
                    if (riff != "RIFF")
                    {
                        System.Diagnostics.Debug.WriteLine($"Not a valid WAV file: {_fullPath}");
                        return GetDefaultDuration();
                    }

                    var fileSize = br.ReadUInt32(); // ファイルサイズ - 8
                    var wave = new string(br.ReadChars(4)); // "WAVE"
                    
                    if (wave != "WAVE")
                    {
                        System.Diagnostics.Debug.WriteLine($"Not a valid WAVE file: {_fullPath}");
                        return GetDefaultDuration();
                    }

                    // fmtチャンクを探す
                    while (fs.Position < fs.Length - 8)
                    {
                        var chunkId = new string(br.ReadChars(4));
                        var chunkSize = br.ReadUInt32();

                        if (chunkId == "fmt ")
                        {
                            var audioFormat = br.ReadUInt16();
                            var numChannels = br.ReadUInt16();
                            var sampleRate = br.ReadUInt32();
                            var byteRate = br.ReadUInt32();
                            var blockAlign = br.ReadUInt16();
                            var bitsPerSample = br.ReadUInt16();

                            // dataチャンクを探す
                            fs.Seek(20 + chunkSize, SeekOrigin.Begin); // fmtチャンクの後から開始
                            
                            while (fs.Position < fs.Length - 8)
                            {
                                var dataChunkId = new string(br.ReadChars(4));
                                var dataChunkSize = br.ReadUInt32();

                                if (dataChunkId == "data")
                                {
                                    // 音声の長さを計算
                                    var durationSeconds = (double)dataChunkSize / byteRate;
                                    var durationMs = (int)(durationSeconds * 1000);
                                    
                                    System.Diagnostics.Debug.WriteLine($"Audio duration calculated: {_fullPath} = {durationMs}ms");
                                    return Math.Max(durationMs, 500); // 最低500ms
                                }
                                else
                                {
                                    // 他のチャンクはスキップ
                                    fs.Seek(dataChunkSize, SeekOrigin.Current);
                                }
                            }
                            break;
                        }
                        else
                        {
                            // 他のチャンクはスキップ
                            fs.Seek(chunkSize, SeekOrigin.Current);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading WAV duration: {ex.Message}");
            }

            return GetDefaultDuration();
        }

        private int GetDefaultDuration()
        {
            // ファイル名に基づくデフォルト値
            if (_filePath.Contains("bougomusenno"))
                return 8000; // 8秒
            else if (_filePath.Contains("set_trainnum"))
                return 4000; // 4秒
            else if (_filePath.Contains("set_complete"))
                return 2000; // 2秒
            else
                return 3000; // デフォルト3秒
        }

        public void PlayOnce(float volume)
        {
            Task.Run(() =>
            {
                try
                {
                    if (System.IO.File.Exists(_fullPath))
                    {
                        // NAudioを使用した重ね再生対応の音声再生
                        using (var audioFile = new AudioFileReader(_fullPath))
                        using (var outputDevice = new WaveOutEvent())
                        {
                            // ボリューム設定
                            audioFile.Volume = volume;
                            
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            
                            System.Diagnostics.Debug.WriteLine($"NAudio音声再生開始: {_filePath} (種別: {_audioType})");
                            
                            // 再生完了まで待機
                            while (outputDevice.PlaybackState == PlaybackState.Playing)
                            {
                                Thread.Sleep(100);
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"NAudio音声再生完了: {_filePath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"NAudio playback failed for {_filePath}: {ex.Message}");
                    
                    // フォールバック: 従来のSoundPlayerを使用
                    try
                    {
                        var player = new System.Media.SoundPlayer(_fullPath);
                        player.LoadAsync();
                        player.Play();
                        System.Diagnostics.Debug.WriteLine($"フォールバック音声再生: {_filePath}");
                    }
                    catch (Exception fallbackEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"フォールバック音声再生も失敗: {_filePath} - {fallbackEx.Message}");
                    }
                }
            });
        }

        public void PlayLoop(float volume)
        {
            try
            {
                if (System.IO.File.Exists(_fullPath) && !_isLooping)
                {
                    _isLooping = true;
                    var player = new System.Media.SoundPlayer(_fullPath);
                    
                    lock (_playersLock)
                    {
                        _activePlayers.Add(player);
                    }
                    
                    player.Load();
                    player.PlayLooping();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio loop playback failed: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _isLooping = false;
                
                lock (_playersLock)
                {
                    foreach (var player in _activePlayers)
                    {
                        try
                        {
                            player.Stop();
                            player.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error stopping player: {ex.Message}");
                        }
                    }
                    _activePlayers.Clear();
                }
                
                if (_loopTimer != null)
                {
                    _loopTimer.Dispose();
                    _loopTimer = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio stop failed: {ex.Message}");
            }
        }

        ~AudioWrapper()
        {
            Stop();
        }
    }
}
