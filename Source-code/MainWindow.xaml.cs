using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MusicPlayer
{
    public partial class MainWindow : Window
    {
        private List<string> _musicFiles = new List<string>();
        private int _currentTrackIndex = -1;
        private WaveOutEvent _outputDevice;
        private AudioFileReader _audioFile;
        private bool _isPlaying = false;
        private string _playMode = "По порядку";
        private Stack<int> _previousTrackIndices = new Stack<int>();
        private DispatcherTimer _timer;
        private bool _isReleasingResources = false;
        private bool _isRightClick = false;
        private FileSystemWatcher _fileWatcher;
        private string _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MuS", "settings.txt");
        private List<int> _shuffledIndices;
        private bool _isTrackSelectionInProgress = false;
        public MainWindow()
        {
            InitializeComponent();
            LoadVolumeSetting();
            LoadMusicFiles();
            InitializeFileWatcher();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;

            if (_audioFile != null)
            {
                _audioFile.Volume = (float)VolumeSlider.Value;
            }
        }
        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var renamedItem = MusicList.Items.Cast<ListBoxItem>().FirstOrDefault(item => (string)item.Tag == e.OldFullPath);
                if (renamedItem != null)
                {
                    renamedItem.Content = $"{MusicList.Items.IndexOf(renamedItem) + 1}. {Path.GetFileNameWithoutExtension(e.FullPath)}";
                    renamedItem.Tag = e.FullPath;
                    int index = _musicFiles.IndexOf(e.OldFullPath);
                    if (index >= 0)
                    {
                        _musicFiles[index] = e.FullPath;
                    }
                    if (_currentTrackIndex >= 0 && _currentTrackIndex < _musicFiles.Count && _musicFiles[_currentTrackIndex] == e.FullPath)
                    {
                        UpdateCurrentSongDisplay(renamedItem);
                    }
                }
            });
        }
        public void InitializeFileWatcher()
        {
            string musicFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

            _fileWatcher = new FileSystemWatcher(musicFolderPath)
            {
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _fileWatcher.Created += OnFileCreated;
            _fileWatcher.Deleted += OnFileDeleted;
            _fileWatcher.Renamed += OnFileRenamed;
        }
        private void LoadVolumeSetting()
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    string volumeText = File.ReadAllText(_settingsFilePath);
                    if (double.TryParse(volumeText, out double savedVolume))
                    {
                        VolumeSlider.Value = savedVolume;
                        if (_audioFile != null)
                        {
                            _audioFile.Volume = (float)savedVolume;
                        }

                    }
                    else
                    {
                        VolumeSlider.Value = 0.5;
                    }
                }
                catch
                {

                }
            }
            else
            {
                VolumeSlider.Value = 0.5;
            }
        }
        private void SaveVolumeSetting(double volume)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath));

                File.WriteAllText(_settingsFilePath, volume.ToString());

            }
            catch
            {
            }
        }
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioFile != null)
            {
                _audioFile.Volume = (float)e.NewValue;
                SaveVolumeSetting(e.NewValue);
            }
        }
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                LoadMusicFiles();
            });
        }
        private async void LoadMusicFiles()
        {
            string musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            string[] extensions = { "*.mp3", "*.wav", "*.flac", "*.aac", "*.ogg" };
            _fileWatcher = new FileSystemWatcher(musicFolder);
            _fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
            _fileWatcher.EnableRaisingEvents = true;
            await Task.Run(() =>
            {
                foreach (var extension in extensions)
                {
                    var musicFiles = Directory.GetFiles(musicFolder, extension);
                    for (int i = 0; i < musicFiles.Length; i++)
                    {
                        var file = musicFiles[i];
                        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                        Dispatcher.Invoke(() =>
                        {
                            MusicList.Items.Add(new ListBoxItem
                            {
                                Content = $"{i + 1}. {fileNameWithoutExtension}",
                                Tag = file
                            });
                            _musicFiles.Add(file);
                        });
                    }
                }
            });
            if (_musicFiles.Count == 0)
            {
                ShowError("Не найдено ни одной музыкальной композиции в папке.");
            }
        }
        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            if (IsMusicFile(e.FullPath) && !_musicFiles.Contains(e.FullPath))
            {
                Dispatcher.Invoke(() =>
                {
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(e.FullPath);
                    MusicList.Items.Add(new ListBoxItem
                    {
                        Content = $"{MusicList.Items.Count + 1}. {fileNameWithoutExtension}",
                        Tag = e.FullPath
                    });
                    _musicFiles.Add(e.FullPath);
                });
            }
        }
        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var itemToRemove = MusicList.Items.Cast<ListBoxItem>().FirstOrDefault(item => (string)item.Tag == e.FullPath);
                if (itemToRemove != null)
                {
                    MusicList.Items.Remove(itemToRemove);
                    _musicFiles.Remove(e.FullPath);
                    UpdateTrackNumbers();
                }
            });
        }
        private bool IsMusicFile(string filePath)
        {
            string[] extensions = { ".mp3", ".wav", ".flac", ".aac", ".ogg" };
            string fileExtension = Path.GetExtension(filePath)?.ToLower();
            return extensions.Contains(fileExtension);
        }
        private void ShowError(string message)
        {
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
                Filter = "Audio Files|*.mp3;*.wav;*.flac;*.aac;*.ogg|All Files|*.*",
                Multiselect = true
            };
            if (openFileDialog.ShowDialog() == true)
            {
                string musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                int startIndex = _musicFiles.Count + 1;
                _fileWatcher.EnableRaisingEvents = false;
                Console.WriteLine("fileWatcher отключен для добавления треков");
                foreach (string file in openFileDialog.FileNames)
                {
                    string fileName = Path.GetFileName(file);
                    string destinationPath = Path.Combine(musicFolder, fileName);

                    if (_musicFiles.Contains(destinationPath))
                    {
                        Console.WriteLine($"Трек \"{fileName}\" уже добавлен.");
                        continue;
                    }
                    if (!File.Exists(destinationPath))
                    {
                        try
                        {
                            File.Move(file, destinationPath);
                            Console.WriteLine($"Файл \"{fileName}\" перемещен в {destinationPath}");

                            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(destinationPath);
                            Console.WriteLine("Добавляем трек в MusicList.Items и _musicFiles");
                            MusicList.Items.Add(new ListBoxItem
                            {
                                Content = $"{startIndex}. {fileNameWithoutExtension}",
                                Tag = destinationPath
                            });
                            _musicFiles.Add(destinationPath);
                            Console.WriteLine($"Текущий список _musicFiles: {string.Join(", ", _musicFiles)}");
                            startIndex++;
                        }
                        catch (Exception ex)
                        {
                            ShowError($"Ошибка при перемещении файла: {ex.Message}");
                        }
                    }
                    else
                    {
                        ShowError($"Трек \"{fileName}\" уже существует в папке.");
                    }
                }
                _fileWatcher.EnableRaisingEvents = true;
                Console.WriteLine("fileWatcher включен снова");
            }
        }
        private void UpdateTrackNumbers()
        {
            for (int i = 0; i < MusicList.Items.Count; i++)
            {
                if (MusicList.Items[i] is ListBoxItem item)
                {
                    string fileName = Path.GetFileNameWithoutExtension((string)item.Tag);
                    item.Content = $"{i + 1}. {fileName}";
                }
            }
        }
        private string GetUniqueDestinationPath(string folder, string fileName)
        {
            string destinationPath = Path.Combine(folder, fileName);
            int fileIndex = 1;
            while (File.Exists(destinationPath))
            {
                string newFileName = Path.GetFileNameWithoutExtension(fileName) + $"({fileIndex})" + Path.GetExtension(fileName);
                destinationPath = Path.Combine(folder, newFileName);
                fileIndex++;
            }
            return destinationPath;
        }
        private void MusicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MusicList.SelectedItem is ListBoxItem selectedItem)
            {
                int selectedIndex = MusicList.Items.IndexOf(selectedItem);
                if (_currentTrackIndex == selectedIndex)
                {
                    if (_outputDevice?.PlaybackState == PlaybackState.Paused)
                    {
                        ResumeMusic();
                    }
                    return;
                }
                _currentTrackIndex = selectedIndex;
                UpdateCurrentSongDisplay(selectedItem);
                if (_outputDevice != null && _outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    StopMusic();
                }

                PlayTrack();
            }
            else
            {
                ResetCurrentSongDisplay();
            }
        }
        private void UpdateCurrentSongDisplay(ListBoxItem selectedItem)
        {
            CurrentSongTextBlock.Text = Path.GetFileName((string)selectedItem.Tag);
            CurrentSongTextBlock.Visibility = Visibility.Visible;
            ProgressBar.Visibility = Visibility.Visible;
        }
        private void ResetCurrentSongDisplay()
        {
            _currentTrackIndex = -1;
            CurrentSongTextBlock.Visibility = Visibility.Collapsed;
            ProgressBar.Visibility = Visibility.Collapsed;
        }
        private void ResumeMusic()
        {
            _outputDevice?.Play();
            _isPlaying = true;
            PlayPauseButton.Content = "Пауза";
            _timer.Start();
        }
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _musicFiles.Count)
            {
                ShowError("Пожалуйста, выберите музыкальный файл для воспроизведения.");
                return;
            }

            if (_outputDevice == null)
            {
                PlayTrack();
            }
            else if (_outputDevice.PlaybackState == PlaybackState.Playing)
            {
                PauseMusic();
            }
            else if (_outputDevice.PlaybackState == PlaybackState.Paused)
            {
                ResumeMusic();
            }
        }
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_audioFile != null && _outputDevice != null && _outputDevice.PlaybackState == PlaybackState.Playing)
            {
                UpdateProgressBar();
            }
        }
        private void StopMusic()
        {
            if (_outputDevice != null)
            {
                if (_outputDevice.PlaybackState == PlaybackState.Playing || _outputDevice.PlaybackState == PlaybackState.Paused)
                {
                    _outputDevice.Stop();
                }
                _outputDevice.Dispose();
                _outputDevice = null;
            }
            if (_audioFile != null)
            {
                _audioFile.Dispose();
                _audioFile = null;
            }
        }
        private void PlayTrack()
        {
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _musicFiles.Count || !File.Exists(_musicFiles[_currentTrackIndex]))
            {
                ShowError("Пожалуйста, выберите музыкальный файл для воспроизведения.");
                return;
            }
            try
            {
                _audioFile = new AudioFileReader(_musicFiles[_currentTrackIndex]);
                _audioFile.Volume = (float)VolumeSlider.Value;

                _outputDevice = new WaveOutEvent();
                _outputDevice.Init(_audioFile);
                _outputDevice.Play();

                _isPlaying = true;
                PlayPauseButton.Content = "Пауза";
                InitializeProgressBar();
                CurrentSongTextBlock.Text = Path.GetFileName(_musicFiles[_currentTrackIndex]);
                _timer.Start();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка при воспроизведении трека: {ex.Message}");
            }
        }
        private void ReleasePreviousResources()
        {
            if (_isReleasingResources) return;
            _isReleasingResources = true;

            try
            {
                _timer.Stop();

                if (_outputDevice != null)
                {
                    _outputDevice.Stop();
                    _outputDevice.PlaybackStopped -= OnPlaybackStopped;
                    _outputDevice.Dispose();
                    _outputDevice = null;
                }

                if (_audioFile != null)
                {

                    _audioFile = null;
                }
            }
            finally
            {
                _isReleasingResources = false;
            }
        }
        private void InitializeProgressBar()
        {
            ProgressBar.Value = 0;
            TimeSpan totalDuration = _audioFile.TotalTime;
            ElapsedTimeTextBlock.Text = $"0:00 / {(int)totalDuration.TotalMinutes}:{totalDuration.Seconds:D2}";
            ElapsedTimeTextBlock.Visibility = Visibility.Visible;
        }
        private void PauseMusic()
        {
            _outputDevice?.Pause();
            _isPlaying = false;
            PlayPauseButton.Content = "Возобновить";
            _timer.Stop();
        }
        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (_isReleasingResources) return;

            Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                PlayPauseButton.Content = "Пауза";
                _timer.Stop();
                ProgressBar.Value = 0;

                NextTrack();
            });
        }
        private void UpdateProgressBar()
        {
            ProgressBar.Value = (_audioFile.CurrentTime.TotalSeconds / _audioFile.TotalTime.TotalSeconds) * 100;
            ElapsedTimeTextBlock.Text = $"{(int)_audioFile.CurrentTime.TotalMinutes}:{_audioFile.CurrentTime.Seconds:D2} / {(int)_audioFile.TotalTime.TotalMinutes}:{_audioFile.TotalTime.Seconds:D2}";
        }
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            NextTrack();
        }
        private void NextTrack()
        {
            if (_musicFiles.Count == 0) return;
            ReleasePreviousResources();
            if (_playMode == "Случайно" && _shuffledIndices != null)
            {
                int currentIndex = _shuffledIndices.IndexOf(_currentTrackIndex);
                _currentTrackIndex = _shuffledIndices[(currentIndex + 1) % _shuffledIndices.Count];
            }
            else
            {
                _currentTrackIndex = (_currentTrackIndex + 1) % _musicFiles.Count;
            }
            MusicList.SelectedIndex = _currentTrackIndex;
            PlayTrack();
        }
        private void OrderShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playMode == "По порядку")
            {
                _playMode = "Случайно";
                OrderShuffleButton.Content = "Случайно";
                ShuffleMusicFiles();
            }
            else
            {
                _playMode = "По порядку";
                OrderShuffleButton.Content = "По порядку";
                RestoreMusicFiles();
            }
        }
        private void ShuffleMusicFiles()
        {
            Random rng = new Random();
            _shuffledIndices = Enumerable.Range(0, _musicFiles.Count).OrderBy(x => rng.Next()).ToList();
        }
        private void RestoreMusicFiles()
        {
            _shuffledIndices = null;
        }
        private void RefreshMusicList()
        {
            MusicList.Items.Clear();
            for (int i = 0; i < _musicFiles.Count; i++)
            {
                MusicList.Items.Add(new ListBoxItem
                {
                    Content = $"{i + 1}. {Path.GetFileNameWithoutExtension(_musicFiles[i])}",
                    Tag = _musicFiles[i]
                });
            }
        }
        private void ProgressBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_audioFile == null) return;

            double mouseX = e.GetPosition(ProgressBar).X;
            double progressBarWidth = ProgressBar.ActualWidth;
            double clickPosition = mouseX / progressBarWidth;

            _audioFile.CurrentTime = TimeSpan.FromSeconds(clickPosition * _audioFile.TotalTime.TotalSeconds);
        }
        private void MusicList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (MusicList.SelectedItem == null)
            {
                e.Handled = true;
            }
        }
        private void RenameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (MusicList.SelectedItem is ListBoxItem selectedItem)
            {
                string oldFilePath = (string)selectedItem.Tag;
                string currentName = Path.GetFileNameWithoutExtension(oldFilePath);
                string newName = Microsoft.VisualBasic.Interaction.InputBox("Введите новое имя:", "Переименовать", currentName);
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    string newFilePath = Path.Combine(Path.GetDirectoryName(oldFilePath), newName + Path.GetExtension(oldFilePath));
                    try
                    {
                        File.Move(oldFilePath, newFilePath);
                        selectedItem.Content = $"{MusicList.SelectedIndex + 1}. {newName}";
                        selectedItem.Tag = newFilePath;
                        int fileIndex = _musicFiles.IndexOf(oldFilePath);
                        if (fileIndex >= 0)
                        {
                            _musicFiles[fileIndex] = newFilePath;
                        }
                        if (_currentTrackIndex == fileIndex)
                        {
                            CurrentSongTextBlock.Text = Path.GetFileName(newFilePath);
                        }

                        MusicList.SelectedItem = selectedItem;
                        MusicList.Focus();
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Ошибка при переименовании файла: {ex.Message}");
                    }
                }
            }
        }
        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (MusicList.SelectedItem is ListBoxItem selectedItem)
            {
                string filePath = (string)selectedItem.Tag;

                int selectedIndex = MusicList.SelectedIndex;

                if (_currentTrackIndex >= 0 && _currentTrackIndex < _musicFiles.Count && _musicFiles[_currentTrackIndex] == filePath)
                {
                    PauseMusic();
                    ResetCurrentSongDisplay();
                    ProgressBar.Value = 0;
                    ElapsedTimeTextBlock.Text = "0:00 / 0:00";
                }
                try
                {
                    File.Delete(filePath);
                    MusicList.Items.Remove(selectedItem);
                    _musicFiles.Remove(filePath);

                    if (selectedIndex >= MusicList.Items.Count)
                    {
                        selectedIndex = MusicList.Items.Count - 1;
                    }

                    if (selectedIndex >= 0)
                    {
                        MusicList.SelectedIndex = selectedIndex;
                        MusicList.Focus();
                    }
                }
                catch (Exception ex)
                {
                    ShowError($"Ошибка при удалении файла: {ex.Message}");
                }
            }
        }
    }
}