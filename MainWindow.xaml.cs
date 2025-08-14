using Media_Player.Objects;
using Media_Player.Scripts;
using Microsoft.Win32;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;


namespace Media_Player
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public enum PlayerState
    {
        Paused,
        Playing,
        None     // initial state so the player doesnt increment if the video was paused and unpaused.
    }

    public partial class MainWindow : Window
    {
        public static int media_volume = 15;
        public static int current_file_index = 0;
        public static PlayerState current_state = PlayerState.None;
        private PlaylistPage playlist_page;
        private PlaylistObject? opened_playlist = null;

        public static AppSettings fetched_settings = null;

        public static bool is_looping = false;
        public static bool is_shuffled = false;
        public static bool handling_media = false;
        public static double current_media_seconds = 1;
        public static bool compact_mode_enabled = false;

        public static CompactWindow ?compact_window = null;

        public Thread ?update_seek_bar_thread = null;

        private List<ListViewItem> previous_order = new List<ListViewItem>();
        private List<ListViewItem> ?before_search_list = null;

        public static string format = @"mm\:ss";


        public static ImmutableList<string> VALID_FILE_EXTENSIONS = new List<string> {"mp3", "mp4", "m4a", }.ToImmutableList<string>();


        private void InitialiseMenuItemIcons()
        {
            open_file_menu_btn.Icon = new Image()
            {
                Source = new BitmapImage(new Uri("/Sprites/file_go.png", UriKind.RelativeOrAbsolute))
            };
            open_folder_menu_btn.Icon = new Image()
            {
                Source = new BitmapImage(new Uri("/Sprites/folder_go.png", UriKind.RelativeOrAbsolute))
            };
            clear_menu_btn.Icon = new Image()
            {
                Source = new BitmapImage(new Uri("/Sprites/bin_clear.png", UriKind.RelativeOrAbsolute))
            };
            open_playlist_menu_btn.Icon = new Image()
            {
                Source = new BitmapImage(new Uri("/Sprites/playlist_go.png", UriKind.RelativeOrAbsolute))
            };
            return_menu_btn.Icon = new Image()
            {
                Source = new BitmapImage(new Uri("/Sprites/cross.png", UriKind.RelativeOrAbsolute))
            };
            play_from_playlist_menu_btn.Icon = new Image()
            {
                Source = new BitmapImage(new Uri("/Sprites/menu_play.png", UriKind.RelativeOrAbsolute))
            };
            clear_saved_options_menu_btn.Icon = new Image()
            {
                Source = new BitmapImage(new Uri("/Sprites/bin_brush.png", UriKind.RelativeOrAbsolute))
            };
            change_menu_btn.Icon = new Image()
            {
                Source = new BitmapImage(new Uri("/Sprites/pencil.png", UriKind.RelativeOrAbsolute))
            };
        }
        public void UpdateVideoPositionBar()
        {

            media_position_slider.Dispatcher.Invoke(new Action(() =>
            {
                media_position_slider.Value = media_position_slider.Minimum;
                current_pos_display.Text = "00:00";
            }));

            if (compact_window != null)
            {
                compact_window.total_display.Dispatcher.Invoke(new Action(() =>
                {
                    compact_window.media_pos_bar.Value = compact_window.media_pos_bar.Minimum;
                    compact_window.current_display.Text = "00:00";
                }));

            }

            while (current_state == PlayerState.Playing)
            {
                if (compact_mode_enabled && compact_window != null)
                {
                    Slider compact_slider = compact_window.media_pos_bar;
                    TextBlock compact_curr_dis = compact_window.current_display;
                    TextBlock compact_total_dis = compact_window.total_display;

                    compact_curr_dis.Dispatcher.Invoke(new Action(() =>
                    {
                        TimeSpan video_span = video_out_display.Position;
                        compact_slider.Value = (video_span.TotalSeconds / current_media_seconds) * 100;

                        compact_curr_dis.Text = video_out_display.Position.ToString(format);
                        if (video_out_display.NaturalDuration.HasTimeSpan)
                        {
                            compact_window.total_display.Text = video_out_display.NaturalDuration.TimeSpan.ToString(format);
                        }

                        if (compact_slider.Value >= compact_slider.Maximum)
                        {
                            compact_slider.Value = compact_slider.Minimum;
                        }
                    }));
                }
                else
                {
                    current_pos_display.Dispatcher.Invoke(new Action(() =>
                    {
                        TimeSpan video_span = video_out_display.Position;
                        media_position_slider.Value = (video_span.TotalSeconds / current_media_seconds) * 100;

                        current_pos_display.Text = video_out_display.Position.ToString(format);

                        if (media_position_slider.Value >= media_position_slider.Maximum)
                        {
                            media_position_slider.Value = media_position_slider.Minimum;
                        }

                    }));
                }

                Thread.Sleep(1000); // each second updates the bar
            }
        }
        public void Shuffle() // shuffles playlist items using the fisher-yates shuffle
        {
            Random random = new Random();
            if (previous_order.Count <= 0)
            {
                foreach (ListViewItem x in playlist_contents.Items)
                {
                    previous_order.Add(x);
                }
            }
            playlist_contents.Items.Clear();
            List<ListViewItem> copy = new List<ListViewItem>(previous_order);
            for (int i = 0; i < previous_order.Count - 1; i++)
            {
                int randInt = random.Next(i + 1);
                ListViewItem temp = copy[i];
                copy[i] = copy[randInt];
                copy[randInt] = temp;
            }
            foreach (ListViewItem item in copy)
            {
                ListViewItem new_item = new ListViewItem();
                new_item.Name = item.Name;
                new_item.Content = item.Content;
                new_item.Tag = item.Tag;
                playlist_contents.Items.Add(new_item);
            }
        }

        private void LoadFiles(string[] file_names, bool display_invalid = true)
        {
            List<string> invalid_paths = new List<string>();
            int count = 0;
            playlist_contents.Items.Clear();
            foreach (string file_name in file_names)
            {
                if (System.IO.Path.Exists(file_name) && VALID_FILE_EXTENSIONS.Contains(System.IO.Path.GetExtension(file_name).ToLower().Replace(".", "")))
                {
                    string actual_name = System.IO.Path.GetFileName(file_name); // gets just the file name and extension to display while keeping the actual path
                    ListViewItem playlist_item = new ListViewItem();
                    playlist_item.Name = $"Item{count}";
                    playlist_item.Content = actual_name;
                    playlist_item.Tag = file_name;
                    count++;
                    playlist_contents.Items.Add(playlist_item);
                }
                else if (!System.IO.Path.Exists(file_name) && VALID_FILE_EXTENSIONS.Contains(System.IO.Path.GetExtension(file_name).ToLower().Replace(".", "")))
                {
                    invalid_paths.Add(file_name);
                }
            }
            playlist_items_display.Text = $"Items: {count}";
            if (playlist_page != null)
            {
                playlist_page.opened_files_list.Items.Clear();
                foreach (ListViewItem item in playlist_contents.Items)
                {
                    ListViewItem new_item = new ListViewItem();
                    new_item.Name = item.Name;
                    new_item.Content = item.Content;
                    new_item.Tag = item.Tag;
                    playlist_page.opened_files_list.Items.Add(new_item);
                }
            }

            before_search_list = playlist_contents.Items.Cast<ListViewItem>().ToList();

            if (invalid_paths.Count > 0 && display_invalid)
            {
                string outP = string.Empty;
                foreach (string f in  invalid_paths)
                {
                    outP += $"{f}\n";
                }
                MessageBox.Show($"""
                    ! WARNING !

                    Found {invalid_paths.Count} invalid paths:
                    {outP}
                    """, $"{invalid_paths.Count} invalid paths found", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task PlayMedia(string media_file_name, bool overwrite = false, bool increment = true, bool auto_play = true)
        {
            handling_media = true;
            string extension = System.IO.Path.GetExtension(media_file_name);
            if (extension != ".mp4")
            {
                video_out_display.Visibility = Visibility.Hidden;
                audio_file_image_display.Visibility = Visibility.Visible;
                audio_file_extension_display.Visibility = Visibility.Visible;
                audio_file_extension_display.Text = extension;
            }
            else
            {
                video_out_display.Visibility = Visibility.Visible;
                audio_file_image_display.Visibility = Visibility.Hidden;
                audio_file_extension_display.Visibility = Visibility.Hidden;
            }

            if (auto_play)
            {
                current_state = PlayerState.Playing;
            }
            else
            {
                current_state = PlayerState.Paused;
            }

            string name = System.IO.Path.GetFileName(media_file_name).Replace(System.IO.Path.GetExtension(media_file_name), "");
            if (overwrite || video_out_display.Source == null)
            {
                video_out_display.Source = null;
                video_out_display.Position = TimeSpan.Zero;
                await Task.Delay(150);
                video_out_display.Source = new Uri(media_file_name);
                media_title_display.Text = name;
            } 
            video_out_display.Volume = volume_slider.Value / 100;
            
            if (auto_play)
            {
                video_out_display.Play();
            }

            if (opened_playlist != null)
            {
                playlist_display.Text = $"Playlist: {opened_playlist.name}";
                song_plays_display.Visibility = Visibility.Visible;
                song_plays_display.IsEnabled = true;
                song_plays_display.Text = $"Media plays: {UtilityHandler.GetSongPlays(media_file_name, opened_playlist)}";
            }

            if (increment && opened_playlist != null && auto_play)
            {
                UtilityHandler.IncrementSong(media_file_name, opened_playlist);
            }
            else if (opened_playlist == null)
            {
                song_plays_display.Visibility = Visibility.Hidden;
                playlist_display.Text = "No playlist selected";
            }

            if (update_seek_bar_thread == null || !update_seek_bar_thread.IsAlive && auto_play)
            {
                update_seek_bar_thread = new Thread(UpdateVideoPositionBar);
                update_seek_bar_thread.IsBackground = true;
                update_seek_bar_thread.Start();
            }
            else if (update_seek_bar_thread.ThreadState == ThreadState.Unstarted && auto_play)
            {
                update_seek_bar_thread.Start();
            }
            handling_media = false;
        }
        public TimeSpan CalculateNewTimespan()
        {
            double slider_pos = Math.Clamp(media_position_slider.Value, media_position_slider.Minimum, media_position_slider.Maximum);
            double media_duration = video_out_display.NaturalDuration.TimeSpan.TotalSeconds;

            double target_seconds = (slider_pos / media_position_slider.Maximum) * media_duration;

            return TimeSpan.FromSeconds(target_seconds);
        }
        public void Seek()
        {
            if (!compact_mode_enabled)
            {
                current_state = PlayerState.Playing;
                video_out_display.Pause();
                TimeSpan new_timespan = CalculateNewTimespan();
                video_out_display.Position = new_timespan;
                update_seek_bar_thread = new Thread(UpdateVideoPositionBar);
                update_seek_bar_thread.IsBackground = true;
                update_seek_bar_thread.Start();
                video_out_display.Play();
            }
            else
            {
                current_state = PlayerState.Playing;
                video_out_display.Pause();
                TimeSpan new_timespan = compact_window.CalculateNewTimespan();
                video_out_display.Position = new_timespan;
                update_seek_bar_thread = new Thread(UpdateVideoPositionBar);
                update_seek_bar_thread.IsBackground = true;
                update_seek_bar_thread.Start();
                video_out_display.Play();
            }
        }
        private void media_position_slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            Seek();
        }
        private void media_position_slider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            current_state = PlayerState.Paused; // stops the update thread
        }

        private void video_out_display_MediaOpened(object sender, RoutedEventArgs e)
        {
            double total_minutes = video_out_display.NaturalDuration.TimeSpan.TotalMinutes;
            if (total_minutes >= 60)
            {
                format = @"hh\:mm\:ss";
            }
            else
            {
                format = @"mm\:ss";
            }
            if (compact_mode_enabled && compact_window != null)
            {
                compact_window.total_display.Text = video_out_display.NaturalDuration.TimeSpan.ToString(format);
                compact_window.current_display.Text = string.Format(format, 0);
                current_media_seconds = video_out_display.NaturalDuration.TimeSpan.TotalSeconds;
                if (playlist_contents.Items.Count > 0)
                {
                    compact_window.song_title.Text = System.IO.Path.GetFileNameWithoutExtension(((ListViewItem)playlist_contents.Items[current_file_index]).Tag.ToString());
                    compact_window.current_playlist.Text = playlist_display.Text;
                    if (current_file_index + 1 < playlist_contents.Items.Count)
                    {
                        compact_window.next_song_display.Text = System.IO.Path.GetFileNameWithoutExtension(((ListViewItem)playlist_contents.Items[current_file_index + 1]).Tag.ToString());
                    }
                    else
                    {
                        compact_window.next_song_display.Text = System.IO.Path.GetFileNameWithoutExtension(((ListViewItem)playlist_contents.Items[0]).Tag.ToString());
                    }
                }
            }
            else
            {
                total_pos_display.Text = video_out_display.NaturalDuration.TimeSpan.ToString(format);
                current_pos_display.Text = string.Format(format, 0);
                current_media_seconds = video_out_display.NaturalDuration.TimeSpan.TotalSeconds;
            }
        }

        private async void ActionMedia(bool increment = true)
        {
            if (playlist_contents.Items.Count > 0)
            {
                handling_media = true;
                current_state = PlayerState.Paused;

                ListViewItem? selected = playlist_contents.Items[current_file_index] as ListViewItem;
                if (selected != null)
                {
                    await PlayMedia(selected.Tag.ToString(), overwrite: true, increment: increment);
                    if (compact_mode_enabled && compact_window != null)
                    {
                        compact_window.current_display.Text = string.Format(format, 0);
                        compact_window.song_title.Text = selected.Tag.ToString();
                        compact_window.current_playlist.Text = playlist_display.Text;
                        if (current_file_index + 1 < playlist_contents.Items.Count)
                        {
                            compact_window.next_song_display.Text = System.IO.Path.GetFileName(((ListViewItem)playlist_contents.Items[current_file_index + 1]).Tag.ToString());
                        }
                        else
                        {
                            compact_window.next_song_display.Text = System.IO.Path.GetFileName(((ListViewItem)playlist_contents.Items[0]).Tag.ToString());
                        }
                    }
                }
            }
        }

        private void Video_out_display_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (handling_media) return;

            current_state = PlayerState.Paused;
            
            if (!is_looping)
            {
                current_file_index++;
            }

            if (current_file_index >= playlist_contents.Items.Count)
            {
                current_file_index = 0;
            }
            Console.WriteLine($"Ended video, index: {current_file_index}");
            update_seek_bar_thread = null;
            ActionMedia();
        }

        public MainWindow()
        {
            InitializeComponent();
            InitialiseMenuItemIcons();
            video_out_display.LoadedBehavior = MediaState.Manual;

            fetched_settings = AppHandler.InitSettings();

            if (fetched_settings.save_files)
            {
                List<string> previouslySaved = SettingsHandler.GetPreviouslySavedFiles();
                if (previouslySaved.Count > 0)
                {
                    foreach (string s in previouslySaved)
                    {
                        Console.WriteLine(s);
                    }
                    LoadFiles(previouslySaved.ToArray(), false);
                }
            }

            if (fetched_settings.resume_on_enter)
            {
                (string fetched_path, double fetched_position) fetched_media_data = SettingsHandler.GetMediaData();
                if (!string.IsNullOrEmpty(fetched_media_data.fetched_path) && System.IO.Path.Exists(fetched_media_data.fetched_path))
                {
                    TimeSpan video_span = video_out_display.Position;
                    PlayMedia(fetched_media_data.fetched_path, true, true, false);
                    video_out_display.Position = TimeSpan.FromSeconds(fetched_media_data.fetched_position);
                    media_position_slider.Value = (video_span.TotalSeconds / fetched_media_data.fetched_position) * 100;
                    Console.WriteLine(media_position_slider.Value);
                    current_pos_display.Text = video_out_display.Position.ToString(format);
                    current_state = PlayerState.None;
                }
            }

            playlist_page = new PlaylistPage(playlist_contents.Items);
            page_display_frame.Navigate(playlist_page);
            page_display_frame.IsEnabled = false;
            page_display_frame.Visibility = Visibility.Hidden;
            video_out_display.MediaEnded += Video_out_display_MediaEnded;
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            // settings saving

            if (fetched_settings.save_files && playlist_contents.Items.Count > 0)
            {
                List<string> to_saveList = new List<string>();
                foreach (ListViewItem item in playlist_contents.Items)
                {
                    to_saveList.Add(item.Tag.ToString());
                }
                SettingsHandler.SaveLoadedFiles(to_saveList);
            }

            if (fetched_settings.resume_on_enter && video_out_display.Source != null)
            {
                string current_source = video_out_display.Source.LocalPath;
                double position = video_out_display.Position.TotalSeconds;

                if (!string.IsNullOrEmpty(current_source))
                {
                    SettingsHandler.SaveMediaData(current_source, position);
                }
            }

            if (compact_mode_enabled && compact_window != null)
            {
                compact_window.Close();
            }
        }

        public void Rewind()
        {
            current_state = PlayerState.Paused;
            ActionMedia(false);
        }
        private void rewind_current_song_btn_Click(object sender, RoutedEventArgs e)
        {
            Rewind();
        }

        public void Previous()
        {
            if (playlist_contents.Items.Count > 1)
            {
                current_file_index--;
                if (current_file_index < 0)
                {
                    current_file_index = 0;
                }
                ActionMedia();
            } 
        }
        private void previous_song_btn_Click(object sender, RoutedEventArgs e)
        {
            Previous();
        }
        private async void pause_btn_Click(object sender, RoutedEventArgs e)
        {
            if (current_state == PlayerState.Paused || current_state == PlayerState.None)
            {
                if (current_file_index < playlist_contents.Items.Count)
                {
                    current_state = PlayerState.Playing;
                    pause_btn.Content = new Image
                    {
                        Source = new BitmapImage(new Uri("/Sprites/pause.png", UriKind.RelativeOrAbsolute))
                    };
                    ListViewItem? media_item = playlist_contents.Items[current_file_index] as ListViewItem;

                    if (media_item != null)
                    {
                        bool to_increment = (current_state == PlayerState.None) ? true : false;
                        await PlayMedia(media_item.Tag.ToString(), increment: to_increment);
                    }
                    else
                    {
                        MessageBox.Show($"Something went wrong while attempting to fetch object at {current_file_index}", "Couldn't fetch object at index", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

            }
            else if (current_state == PlayerState.Playing)
            {
                current_state = PlayerState.Paused;
                pause_btn.Content = new Image
                {
                    Source = new BitmapImage(new Uri("/Sprites/play.png", UriKind.RelativeOrAbsolute))
                };
                video_out_display.Pause();
            }
        }

        public void Skip()
        {
            if (playlist_contents.Items.Count > 0)
            {
                current_state = PlayerState.Paused;
                current_file_index++;
                if (current_file_index >= playlist_contents.Items.Count)
                {
                    current_file_index = 0;
                }
                ActionMedia();
            }
        }
        private void skip_song_btn_Click(object sender, RoutedEventArgs e)
        {
            Skip();
        }

        private void open_file_menu_btn_Click(object sender, RoutedEventArgs e)
        {
           OpenFileDialog dialog = new OpenFileDialog();
            dialog.Multiselect = true;
            dialog.InitialDirectory = AppContext.BaseDirectory;
            dialog.Title = "Select files to load";
            previous_order.Clear();
            if (dialog.ShowDialog() == true)
            {
                string[] fileName = dialog.FileNames;
                LoadFiles(fileName);
                foreach (ListViewItem item in playlist_contents.Items)
                {
                    previous_order.Add(item);
                }
            }
        }

        private void open_folder_menu_btn_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                string[] file_names = Directory.GetFiles(dialog.FolderName);
                LoadFiles(file_names);
                previous_order.Clear();
                foreach (ListViewItem item in playlist_contents.Items)
                {
                    previous_order.Add(item);
                }
            }
        }

        private void open_playlist_menu_btn_Click(object sender, RoutedEventArgs e)
        {
            media_controls.Visibility = Visibility.Hidden;
            media_controls.IsEnabled = false;
            page_display_frame.Visibility = Visibility.Visible;
            page_display_frame.IsEnabled = true;
        }

        private void return_menu_btn_Click(object sender, RoutedEventArgs e)
        {
            media_controls.Visibility = Visibility.Visible;
            media_controls.IsEnabled = true;
            page_display_frame.Visibility = Visibility.Hidden;
            page_display_frame.IsEnabled = false;

            if (playlist_page != null)
            {
                playlist_page.opened_files_list.Dispatcher.Invoke(new Action(() =>
                {
                    playlist_page.opened_files_list.Items.Clear();
                }));
            }
        }

        private static bool created_playlistplay = false;
        private void play_from_playlist_menu_btn_Click(object sender, RoutedEventArgs e)
        {
            if (!created_playlistplay)
            {
                List<PlaylistObject> fetched_playlists = PlaylistHandler.LoadPlaylists();
                if (fetched_playlists.Count <= 0)
                {
                    MessageBox.Show("Please create a playlist first!", "No playlists found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                created_playlistplay = true;
                SelectPlaylistWindow new_window = new SelectPlaylistWindow(fetched_playlists);
                if (new_window.ShowDialog() == true)
                {
                    PlaylistObject? selected = PlaylistHandler.selected_playlist;
                    if (selected != null)
                    {
                        LoadFiles(selected.playlist_items.ToArray());
                        opened_playlist = selected;
                        created_playlistplay = false;
                    }
                }
            }
        }

        private void clear_menu_btn_Click(object sender, RoutedEventArgs e)
        {
            playlist_contents.Items.Clear();
            playlist_items_display.Text = "Items: 0";
        }

        public void UpdateVolume()
        {
            volume_display.Text = $"Volume: {((int)volume_slider.Value).ToString()}";
            video_out_display.Volume = volume_slider.Value / 100;
        }
        private void volume_slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateVolume();
        }

        public void ToggleRepeat()
        {
            if (!is_looping)
            {
                repeat_btn.Content = new Image
                {
                    Source = new BitmapImage(new Uri("/Sprites/repeat_triggered.png", UriKind.RelativeOrAbsolute))
                };
                is_looping = true;
            }
            else
            {
                repeat_btn.Content = new Image
                {
                    Source = new BitmapImage(new Uri("/Sprites/repeat_untriggered.png", UriKind.RelativeOrAbsolute))
                };
                is_looping = false;
            }
        }
        private void repeat_btn_Click(object sender, RoutedEventArgs e)
        {
            ToggleRepeat();
        }
        public void ShuffleMain()
        {
            if (playlist_contents.Items.Count > 0)
            {
                if (!is_shuffled)
                {
                    is_shuffled = true;
                    Shuffle();
                    shuffle_btn.Content = new Image
                    {
                        Source = new BitmapImage(new Uri("/Sprites/shuffle_triggered.png", UriKind.RelativeOrAbsolute))
                    };
                }
                else if (is_shuffled && previous_order.Count > 0)
                {
                    is_shuffled = false;
                    string file_name = media_title_display.Text;
                    int c = 0;
                    foreach (ListViewItem item in previous_order)
                    {
                        if (item.Content.ToString().Contains(file_name))
                        {
                            current_file_index = c;
                            break;
                        }
                        Console.WriteLine(c);
                        c++;
                    }
                    shuffle_btn.Content = new Image
                    {
                        Source = new BitmapImage(new Uri("/Sprites/shuffle_untriggered.png", UriKind.RelativeOrAbsolute))
                    };
                    playlist_contents.Items.Clear();
                    foreach (ListViewItem prev_item in previous_order)
                    {
                        ListViewItem new_item = new ListViewItem();
                        new_item.Name = prev_item.Name;
                        new_item.Tag = prev_item.Tag;
                        new_item.Content = prev_item.Content;
                        playlist_contents.Items.Add(new_item);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please add items before attempting to shuffle!", "No items to shuffle", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void shuffle_btn_Click(object sender, RoutedEventArgs e)
        {
            ShuffleMain();            
        }

        private async void playlist_contents_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListViewItem? selected_item = playlist_contents.SelectedItem as ListViewItem;
            if (selected_item != null)
            {
                current_file_index = playlist_contents.SelectedIndex;
                current_state = PlayerState.Paused;
                await PlayMedia(selected_item.Tag.ToString(), true, true);
            }
        }

        private void RestoreItemsBeforeSearch()
        {
            if (before_search_list != null)
            {
                playlist_contents.Items.Clear();
                foreach (ListViewItem item in before_search_list)
                {
                    playlist_contents.Items.Add(item);
                }
            }
        }
        private void opened_searchbox_TextChanged(object sender, TextChangedEventArgs e)
        {

            if (string.IsNullOrEmpty(opened_searchbox.Text))
            {
                RestoreItemsBeforeSearch();
                return;
            }

            List<ListViewItem> results = new List<ListViewItem>();

            foreach (ListViewItem item in playlist_contents.Items)
            {
                if (item.Tag.ToString().ToLower().Contains(opened_searchbox.Text.ToLower()))
                {
                    results.Add(item);
                }
            }

            if (results.Count > 0)
            {
                playlist_contents.Items.Clear();
                foreach (ListViewItem result in  results)
                {
                    playlist_contents.Items.Add(result);
                }
            }
            else if (results.Count <= 0)
            {
                RestoreItemsBeforeSearch();
            }
        }

        private void info_menu_btn_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("""
                Media Player v1.0.0

                Getting started:
                To start using the media player you can either open a folder or some files to add to the list.
                You can also create a playlist.

                The difference between adding regular files or folders and loading a playlist is that media files in playlists will have their play counts increased.

                Supported file formats: Most file formats supported by the default Windows Media Player (mp3, mp4, m4a, etc.)

                Options panel:

                The options panel allows you to toggle various features such as file list saving which will load all the items in the list on next startup.
                This won't load the playlist if they were apart of one!
                You can also toggle resume on enter which will automatically resume the last media that was being played by the player at the exact
                position. (Will not automatically play it, just load it)

                Compact mode:
                
                Compact mode allows you to get rid of the gigantic media player interface with useless features for general listening.
                You can also toggle always on top for compact mode which will display it over any other processes.
                """);
        }

        public static bool createdOptions = false;
        private void options_menu_btn_Click(object sender, RoutedEventArgs e)
        {
            if (!createdOptions)
            {
                createdOptions = true;
                OptionsWindow newOptions = new OptionsWindow(this);
                newOptions.Show();
            }
        }

        private void clear_saved_options_menu_btn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you wish to clear all options data? (saved files, resume on play)\nThis action cannot be reversed!", "Clear options data", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SettingsHandler.Clear();
                fetched_settings.save_files = false;
                fetched_settings.resume_on_enter = false;
            }
        }

        private void mute_unmute_btn_Click(object sender, RoutedEventArgs e)
        {
            if (volume_slider.Value > 0)
            {
                media_volume = (int)volume_slider.Value;
                volume_slider.Value = 0;
                UpdateVolume();
                mute_unmute_btn.Content = new Image()
                {
                    Source = new BitmapImage(new Uri("/Sprites/mute.png", UriKind.RelativeOrAbsolute))
                };
            }
            else if (volume_slider.Value < 1)
            {
                volume_slider.Value = media_volume;
                UpdateVolume();
                mute_unmute_btn.Content = new Image()
                {
                    Source = new BitmapImage(new Uri("/Sprites/unmute.png", UriKind.RelativeOrAbsolute))
                };
            }
        }
    }
}