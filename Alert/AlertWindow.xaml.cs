﻿using MahApps.Metro;
using MahApps.Metro.Controls;
using Nortal.Utilities.Csv;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Alert
{
    public partial class AlertWindow : MetroWindow, INotifyPropertyChanged
    {
        #region Fields

        private double windowHeight, windowWidth, windowTop, windowLeft;

        private bool stopPipeServer = false;

        private Alert _selectedAlert;

        #endregion Fields

        #region Constructor

        public AlertWindow()
        {
            Loaded += MetroWindow_Loaded;
            Closing += MetroWindow_Closing;
            SizeChanged += MetroWindow_SizeChanged;
            LocationChanged += MetroWindow_LocationChanged;

            this.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri("pack://application:,,,/Alert;component/Resources/Icons.xaml", UriKind.RelativeOrAbsolute) });
            this.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml", UriKind.RelativeOrAbsolute) });
            this.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml", UriKind.RelativeOrAbsolute) });
            this.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Colors.xaml", UriKind.RelativeOrAbsolute) });
            this.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri(string.Format("pack://application:,,,/MahApps.Metro;component/Styles/Accents/{0}.xaml", Manager.CurrentAccent.Name), UriKind.RelativeOrAbsolute) });
            this.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri(string.Format("pack://application:,,,/MahApps.Metro;component/Styles/Accents/{0}.xaml", Manager.CurrentTheme.Name), UriKind.RelativeOrAbsolute) });

            InitializeComponent();

            Alerts = new ObservableCollection<Alert>();
        }

        #endregion Constructor

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion Events

        #region Properties

        public ObservableCollection<Alert> Alerts { get; set; }

        public Alert SelectedAlert
        {
            get
            {
                return _selectedAlert;
            }
            set
            {
                _selectedAlert = value;

                OnPropertyChanged("SelectedAlert");
            }
        }

        #endregion Properties

        #region Methods

        public void Invoke(Action action)
        {
            this.Dispatcher.BeginInvoke(action);
        }

        private void BringInFront()
        {
            if (this.Visibility == Visibility.Hidden || this.Visibility == Visibility.Collapsed)
            {
                this.Visibility = Visibility.Visible;
            }

            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;

            handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            GetAlertsFromFile();

            StartPipeListening();
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
            {
                Registry.SetValue("IsWindowSizeSaved", true);

                if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Minimized)
                {
                    Registry.SetValue("WindowHeight", windowHeight);
                    Registry.SetValue("WindowWidth", windowWidth);
                    Registry.SetValue("WindowTop", windowTop);
                    Registry.SetValue("WindowLeft", windowLeft);
                }
            }

            if (Manager.Mutex != null)
            {
                Manager.Mutex.Close();
            }

            stopPipeServer = true;

            Manager.StopPipeServer();
        }

        private void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal && this.Visibility == Visibility.Visible)
            {
                windowHeight = e.NewSize.Height;
                windowWidth = e.NewSize.Width;
            }
        }

        private void MetroWindow_LocationChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Normal && this.Visibility == Visibility.Visible)
            {
                windowTop = Top;
                windowLeft = Left;
            }
        }

        private void RemoveAllAlertsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                File.WriteAllText(Manager.FilePath, string.Empty);

                Alerts.Clear();
            }
            catch (Exception ex)
            {
                Manager.LogException(ex);
            }
        }

        private void RemoveAlertButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedAlert != null)
                {
                    Alerts.Remove(SelectedAlert);

                    File.WriteAllText(Manager.FilePath, string.Empty);

                    using (StringWriter writer = new StringWriter())
                    {
                        CsvWriter csv = new CsvWriter(writer, new CsvSettings());

                        foreach (Alert alert in Alerts)
                        {
                            csv.WriteLine(alert.TradeSide, alert.Symbol, alert.TimeFrame, alert.Time.ToString(), alert.Comment);
                        }

                        File.AppendAllText(Manager.FilePath, writer.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Manager.LogException(ex);
            }
        }

        private void GetAlertsFromFile()
        {
            Alerts.Clear();

            try
            {
                using (var parser = new CsvParser(File.ReadAllText(Manager.FilePath)))
                {
                    foreach (String[] line in parser.ReadToEnd())
                    {
                        Alerts.Add(new Alert()
                        {
                            TradeSide = line[0],
                            Symbol = line[1],
                            TimeFrame = line[2],
                            Time = DateTime.Parse(line[3], CultureInfo.InvariantCulture),
                            Comment = line[4]
                        });
                    }
                }

                if (Alerts.Count > Manager.MaximumAlertsNumberToShow)
                {
                    File.WriteAllText(Manager.FilePath, string.Empty);

                    int counter = Alerts.Count - Manager.MaximumAlertsNumberToShow;

                    Alerts.ToList().ForEach(alert => 
                    {
                        if (counter > 0)
                        {
                            Alerts.Remove(alert);
                            counter--;
                        }
                    });

                    Alerts.ToList().ForEach(alert => Manager.WriteAlert(alert));
                }

                SelectedAlert = Alerts.LastOrDefault();

                AlertsDataGrid.UpdateLayout();
                AlertsDataGrid.ScrollIntoView(SelectedAlert, null);
            }
            catch (Exception ex)
            {
                Manager.LogException(ex);

                File.Delete(Manager.FilePath);
                File.Create(Manager.FilePath).Close();
            }

            BringInFront();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settingsWindow = new SettingsWindow();

            this.IsEnabled = false;

            settingsWindow.ShowDialog();

            ThemeManager.ChangeAppStyle(this, Manager.CurrentAccent, Manager.CurrentTheme);

            this.IsEnabled = true;
        }

        private void StartPipeListening()
        {
            Thread pipeThread = new Thread(new ThreadStart(() =>
            {
                try
                {
                    while (!stopPipeServer)
                    {
                        using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(Properties.Settings.Default.PipeServerName))
                        {
                            pipeServer.WaitForConnection();

                            int result = pipeServer.ReadByte();

                            switch (result)
                            {
                                case 0:
                                    {
                                        stopPipeServer = true;

                                        break;
                                    }
                                case 1:
                                    {
                                        Invoke(new Action(() =>
                                        {
                                            GetAlertsFromFile();
                                        }));

                                        break;
                                    }
                                default:
                                    break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Manager.LogException(ex);

                    Manager.StopPipeServer();
                }

                if (!stopPipeServer)
                {
                    StartPipeListening();
                }
            }));

            pipeThread.CurrentCulture = CultureInfo.InvariantCulture;
            pipeThread.CurrentUICulture = CultureInfo.InvariantCulture;

            pipeThread.Start();
        }

        private void SetWindowSize()
        {
            bool isWindowSizeSaved = bool.Parse(Registry.GetValue("IsWindowSizeSaved", false));

            if (isWindowSizeSaved && WindowState != WindowState.Maximized)
            {
                double savedHeight = double.Parse(Registry.GetValue("WindowHeight", 0), CultureInfo.InvariantCulture);
                double savedWidth = double.Parse(Registry.GetValue("WindowWidth", 0), CultureInfo.InvariantCulture);
                double savedTop = double.Parse(Registry.GetValue("WindowTop", 0), CultureInfo.InvariantCulture);
                double savedLeft = double.Parse(Registry.GetValue("WindowLeft", 0), CultureInfo.InvariantCulture);

                Height = savedHeight != 0 ? savedHeight : Height;
                Width = savedWidth != 0 ? savedWidth : Width;
                Top = savedTop != 0 ? savedTop : Top;
                Left = savedLeft != 0 ? savedLeft : Left;
            }
        }

        #endregion Methods
    }
}